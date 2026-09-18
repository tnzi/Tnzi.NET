using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.SignalR.Hubs;
using Tnzi.SignalR.Tests.TestDoubles;

namespace Tnzi.SignalR.Tests.Hubs;

/// <summary>
/// <see cref="TnziHub"/> 的可选服务解析来源。
///
/// ★ 此前 <c>IEventBus</c> / <c>ILoggerFactory</c> / <c>IHubConnectionAborter</c> /
/// 无参 Hub 的 <c>IConnectionManager</c> 全部从 <c>Context.GetHttpContext().RequestServices</c>
/// 惰性解析。那个来源在长轮询传输下靠不住：SignalR 给长轮询连接的是一份克隆的
/// <c>HttpContext</c>，它的请求服务作用域随连接释放而释放，而 <c>OnDisconnectedAsync</c>
/// 在那之后才跑 —— 于是断开事件不发、presence 永远不下线、中断表不登记，零日志。
///
/// 可靠来源是 Hub 激活作用域：<see cref="HubInvocationServicesFilter"/> 在每次
/// 连接 / 断开 / 方法调用期间把它放进 <c>Context.Items</c>，任何传输都有。
/// 这批测试里 <c>HttpContext.RequestServices</c> 一律给一个**空**提供器（模拟克隆体），
/// 服务只能从过滤器暴露的那份里拿到。
/// </summary>
public class TnziHubInvocationServicesTests
{
    private sealed class ParameterlessHub : TnziHub
    {
    }

    private sealed class NullGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class RecordingLoggerFactory(RecordingLogger logger) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => logger;
        public void Dispose() { }
    }

    private static ClaimsPrincipal UserWithId(Guid id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test"));

    /// <summary>
    /// 请求服务是空的（长轮询克隆体的形状），能用的服务只在过滤器暴露的激活作用域里。
    /// </summary>
    private static FakeHubCallerContext ContextWithStarvedRequestServices(Guid userId, IServiceProvider invocationServices)
    {
        var context = new FakeHubCallerContext(
            connectionId: "conn-1",
            user: UserWithId(userId),
            requestServices: new ServiceCollection().BuildServiceProvider());
        context.Items[HubInvocationServicesFilter.ItemKey] = invocationServices;
        return context;
    }

    private static Mock<IConnectionManager> ConnectionManagerWith(int connectionCount)
    {
        var manager = new Mock<IConnectionManager>();
        manager.Setup(m => m.GetConnectionCountAsync(It.IsAny<Guid>())).ReturnsAsync(connectionCount);
        return manager;
    }

    [Fact]
    public async Task PublishesDisconnectedEvent_WhenHttpRequestServicesAreEmpty()
    {
        var userId = Guid.NewGuid();
        var eventBus = new Mock<IEventBus>();
        var services = new ServiceCollection()
            .AddSingleton(eventBus.Object)
            .AddSingleton(ConnectionManagerWith(0).Object)
            .BuildServiceProvider();
        using var hub = new ParameterlessHub();
        hub.Context = ContextWithStarvedRequestServices(userId, services);
        hub.Groups = new NullGroupManager();

        await hub.OnDisconnectedAsync(exception: null);

        eventBus.Verify(
            b => b.PublishAsync(
                It.Is<UserDisconnectedEvent>(e => e.UserId == userId && e.ConnectionId == "conn-1" && e.WentOffline),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishesConnectedEvent_WhenHttpRequestServicesAreEmpty()
    {
        var userId = Guid.NewGuid();
        var eventBus = new Mock<IEventBus>();
        var services = new ServiceCollection()
            .AddSingleton(eventBus.Object)
            .AddSingleton(ConnectionManagerWith(1).Object)
            .BuildServiceProvider();
        using var hub = new ParameterlessHub();
        hub.Context = ContextWithStarvedRequestServices(userId, services);
        hub.Groups = new NullGroupManager();

        await hub.OnConnectedAsync();

        eventBus.Verify(
            b => b.PublishAsync(
                It.Is<UserConnectedEvent>(e => e.UserId == userId && e.ConnectionId == "conn-1" && e.TotalConnections == 1),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RegistersAborter_WhenHttpRequestServicesAreEmpty()
    {
        var aborter = new Mock<IHubConnectionAborter>();
        var services = new ServiceCollection().AddSingleton(aborter.Object).BuildServiceProvider();
        using var hub = new ParameterlessHub();
        hub.Context = ContextWithStarvedRequestServices(Guid.NewGuid(), services);
        hub.Groups = new NullGroupManager();

        await hub.OnConnectedAsync();

        aborter.Verify(a => a.Register("conn-1", hub.Context), Times.Once);
    }

    /// <summary>
    /// 激活作用域已释放（并发调用的边角）时不能炸，也不能因此忘了 HttpContext 那一份。
    /// </summary>
    [Fact]
    public async Task FallsBackToHttpRequestServices_WhenTheExposedScopeIsDisposed()
    {
        var userId = Guid.NewGuid();
        var eventBus = new Mock<IEventBus>();
        var fallback = new ServiceCollection()
            .AddSingleton(eventBus.Object)
            .AddSingleton(ConnectionManagerWith(0).Object)
            .BuildServiceProvider();
        var disposed = new ServiceCollection().BuildServiceProvider();
        disposed.Dispose();

        var context = new FakeHubCallerContext(connectionId: "conn-1", user: UserWithId(userId), requestServices: fallback);
        context.Items[HubInvocationServicesFilter.ItemKey] = disposed;
        using var hub = new ParameterlessHub();
        hub.Context = context;
        hub.Groups = new NullGroupManager();

        await hub.OnDisconnectedAsync(exception: null);

        eventBus.Verify(b => b.PublishAsync(It.IsAny<UserDisconnectedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 事件总线解析不到时必须可见：「模块未加载」「作用域没了」「一切正常」此前在日志里一模一样。
    /// 每条连接只报一次，连接与断开两端共用这一次。
    /// </summary>
    [Fact]
    public async Task LogsWarningOncePerConnection_WhenEventBusIsUnresolvable()
    {
        var userId = Guid.NewGuid();
        var logger = new RecordingLogger();
        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(new RecordingLoggerFactory(logger))
            .AddSingleton(ConnectionManagerWith(1).Object)
            .BuildServiceProvider();
        var context = ContextWithStarvedRequestServices(userId, services);

        using var connectHub = new ParameterlessHub();
        connectHub.Context = context;
        connectHub.Groups = new NullGroupManager();
        await connectHub.OnConnectedAsync();

        using var disconnectHub = new ParameterlessHub();
        disconnectHub.Context = context;
        disconnectHub.Groups = new NullGroupManager();
        await disconnectHub.OnDisconnectedAsync(exception: null);

        logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("IEventBus")).ShouldBe(1);
    }

    [Fact]
    public async Task DoesNotWarn_WhenEventBusResolves()
    {
        var userId = Guid.NewGuid();
        var logger = new RecordingLogger();
        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(new RecordingLoggerFactory(logger))
            .AddSingleton(new Mock<IEventBus>().Object)
            .AddSingleton(ConnectionManagerWith(1).Object)
            .BuildServiceProvider();
        using var hub = new ParameterlessHub();
        hub.Context = ContextWithStarvedRequestServices(userId, services);
        hub.Groups = new NullGroupManager();

        await hub.OnConnectedAsync();

        logger.Entries.ShouldNotContain(e => e.Level == LogLevel.Warning);
    }
}
