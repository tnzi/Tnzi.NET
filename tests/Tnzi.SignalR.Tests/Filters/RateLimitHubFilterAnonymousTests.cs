using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.SignalR.Filters;
using Tnzi.SignalR.Tests.TestDoubles;

namespace Tnzi.SignalR.Tests.Filters;

/// <summary>
/// 匿名连接的限流。
///
/// ★ 分区键此前只有用户 id，取不到就 <c>await next(...)</c> 整条放行。于是匿名 Hub
/// （<c>[AllowAnonymous]</c>，或未要求认证的 Hub 上认证失败的客户端）既不计连接数、
/// 不计消息速率，也不查封禁 —— 而且既没有告警，也没有一个配置项能表明"这里没有限流"。
/// 与 <c>Tnzi.AspNetCore</c> 08-10 修掉的"拿不到分区键就放行"是同一个形状。
/// </summary>
public class RateLimitHubFilterAnonymousTests
{
    private sealed class TestHub : Hub
    {
        public Task Ping() => Task.CompletedTask;
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal LoggedIn(Guid id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test"));

    private static RateLimitHubFilter CreateFilter(IRateLimitService service, RateLimitOptions options) =>
        new(
            Mock.Of<ILogger<RateLimitHubFilter>>(),
            service,
            Microsoft.Extensions.Options.Options.Create(new SignalROptions { RateLimit = options }));

    private static HubLifetimeContext Lifetime(Hub hub, ClaimsPrincipal? user, string? clientIp = "203.0.113.7", string connectionId = "conn-1") =>
        new(
            new FakeHubCallerContext(connectionId: connectionId, user: user, clientIp: clientIp),
            new ServiceCollection().BuildServiceProvider(),
            hub);

    private static HubInvocationContext Invocation(Hub hub, ClaimsPrincipal? user, string? clientIp = "203.0.113.7") =>
        new(
            new FakeHubCallerContext(user: user, clientIp: clientIp),
            new ServiceCollection().BuildServiceProvider(),
            hub,
            hub.GetType().GetMethod("Ping")!,
            []);

    private static Mock<IRateLimitService> PermissiveService()
    {
        var service = new Mock<IRateLimitService>();
        service.Setup(s => s.IsAnonymousBannedAsync(It.IsAny<string>())).ReturnsAsync(false);
        service.Setup(s => s.TryAcquireAnonymousConnectionAsync(It.IsAny<string>())).ReturnsAsync(true);
        service.Setup(s => s.CheckAnonymousMessageRateLimitAsync(It.IsAny<string>())).ReturnsAsync(true);
        return service;
    }

    // ---------- 连接期 ----------

    [Fact]
    public async Task AnonymousConnection_IsCountedAgainstItsPartition()
    {
        var service = PermissiveService();
        var filter = CreateFilter(service.Object, new RateLimitOptions());
        using var hub = new TestHub();

        await filter.OnConnectedAsync(Lifetime(hub, Anonymous()), _ => Task.CompletedTask);

        service.Verify(s => s.TryAcquireAnonymousConnectionAsync("ip:203.0.113.7"), Times.Once);
    }

    [Fact]
    public async Task AnonymousConnection_IsCheckedAgainstTheBanList()
    {
        var service = PermissiveService();
        service.Setup(s => s.IsAnonymousBannedAsync("ip:203.0.113.7")).ReturnsAsync(true);
        var filter = CreateFilter(service.Object, new RateLimitOptions());
        using var hub = new TestHub();
        var nextCalled = false;

        await Should.ThrowAsync<HubException>(() =>
            filter.OnConnectedAsync(Lifetime(hub, Anonymous()), _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            }));

        nextCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task AnonymousConnection_IsRejectedWhenThePartitionIsFull()
    {
        var service = PermissiveService();
        service.Setup(s => s.TryAcquireAnonymousConnectionAsync(It.IsAny<string>())).ReturnsAsync(false);
        var filter = CreateFilter(service.Object, new RateLimitOptions());
        using var hub = new TestHub();

        await Should.ThrowAsync<HubException>(() =>
            filter.OnConnectedAsync(Lifetime(hub, Anonymous()), _ => Task.CompletedTask));
    }

    /// <summary>
    /// 拿不到远端地址时退回按连接分区 —— 弱于按 IP，但远强于整条放行。
    /// </summary>
    [Fact]
    public async Task PartitionFallsBackToTheConnectionId_WhenThereIsNoClientIp()
    {
        var service = PermissiveService();
        var filter = CreateFilter(service.Object, new RateLimitOptions());
        using var hub = new TestHub();

        await filter.OnConnectedAsync(
            Lifetime(hub, Anonymous(), clientIp: null, connectionId: "conn-xyz"),
            _ => Task.CompletedTask);

        service.Verify(s => s.TryAcquireAnonymousConnectionAsync("conn:conn-xyz"), Times.Once);
    }

    [Fact]
    public async Task RejectPolicy_RefusesAnonymousConnectionsOutright()
    {
        var service = PermissiveService();
        var filter = CreateFilter(
            service.Object,
            new RateLimitOptions { AnonymousPolicy = AnonymousHubRateLimitPolicy.Reject });
        using var hub = new TestHub();

        await Should.ThrowAsync<HubException>(() =>
            filter.OnConnectedAsync(Lifetime(hub, Anonymous()), _ => Task.CompletedTask));

        service.Verify(s => s.TryAcquireAnonymousConnectionAsync(It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// <c>Allow</c> 是本修复之前的行为，保留为显式的一次配置选择。
    /// </summary>
    [Fact]
    public async Task AllowPolicy_LeavesAnonymousConnectionsUnlimited()
    {
        var service = PermissiveService();
        var filter = CreateFilter(
            service.Object,
            new RateLimitOptions { AnonymousPolicy = AnonymousHubRateLimitPolicy.Allow });
        using var hub = new TestHub();
        var nextCalled = false;

        await filter.OnConnectedAsync(Lifetime(hub, Anonymous()), _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        nextCalled.ShouldBeTrue();
        service.Verify(s => s.TryAcquireAnonymousConnectionAsync(It.IsAny<string>()), Times.Never);
        service.Verify(s => s.IsAnonymousBannedAsync(It.IsAny<string>()), Times.Never);
    }

    // ---------- 方法调用期 ----------

    [Fact]
    public async Task AnonymousInvocation_IsRateLimitedAndRecorded()
    {
        var service = PermissiveService();
        var filter = CreateFilter(service.Object, new RateLimitOptions());
        using var hub = new TestHub();

        await filter.InvokeMethodAsync(
            Invocation(hub, Anonymous()),
            _ => ValueTask.FromResult<object?>(null));

        service.Verify(s => s.CheckAnonymousMessageRateLimitAsync("ip:203.0.113.7"), Times.Once);
        service.Verify(s => s.RecordAnonymousMessageAsync("ip:203.0.113.7"), Times.Once);
    }

    [Fact]
    public async Task AnonymousInvocation_IsBannedWhenTheRateIsExceeded()
    {
        var service = PermissiveService();
        service.Setup(s => s.CheckAnonymousMessageRateLimitAsync(It.IsAny<string>())).ReturnsAsync(false);
        var options = new RateLimitOptions { BanDuration = TimeSpan.FromMinutes(3) };
        var filter = CreateFilter(service.Object, options);
        using var hub = new TestHub();

        await Should.ThrowAsync<HubException>(async () =>
            await filter.InvokeMethodAsync(
                Invocation(hub, Anonymous()),
                _ => ValueTask.FromResult<object?>(null)));

        service.Verify(s => s.BanAnonymousAsync("ip:203.0.113.7", TimeSpan.FromMinutes(3)), Times.Once);
    }

    /// <summary>
    /// 消息在方法体执行**之前**计数 —— 否则用会抛异常的调用就能绕开计数。
    /// </summary>
    [Fact]
    public async Task AnonymousInvocation_IsCountedEvenWhenTheMethodThrows()
    {
        var service = PermissiveService();
        var filter = CreateFilter(service.Object, new RateLimitOptions());
        using var hub = new TestHub();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await filter.InvokeMethodAsync(
                Invocation(hub, Anonymous()),
                _ => throw new InvalidOperationException("boom")));

        service.Verify(s => s.RecordAnonymousMessageAsync("ip:203.0.113.7"), Times.Once);
    }

    // ---------- 断开 ----------

    [Fact]
    public async Task AnonymousDisconnect_ReleasesTheConnectionSlot()
    {
        var service = PermissiveService();
        var filter = CreateFilter(service.Object, new RateLimitOptions());
        using var hub = new TestHub();
        var context = Lifetime(hub, Anonymous());

        await filter.OnConnectedAsync(context, _ => Task.CompletedTask);
        await filter.OnDisconnectedAsync(context, null, (_, _) => Task.CompletedTask);

        service.Verify(s => s.ReleaseAnonymousConnectionAsync("ip:203.0.113.7"), Times.Once);
    }

    /// <summary>
    /// 名额没占上的连接（被封禁 / 超上限）断开时不得还一次 —— 那会凭空多出额度。
    /// </summary>
    [Fact]
    public async Task RejectedAnonymousConnection_DoesNotReleaseASlotItNeverTook()
    {
        var service = PermissiveService();
        service.Setup(s => s.TryAcquireAnonymousConnectionAsync(It.IsAny<string>())).ReturnsAsync(false);
        var filter = CreateFilter(service.Object, new RateLimitOptions());
        using var hub = new TestHub();
        var context = Lifetime(hub, Anonymous());

        await Should.ThrowAsync<HubException>(() => filter.OnConnectedAsync(context, _ => Task.CompletedTask));
        await filter.OnDisconnectedAsync(context, null, (_, _) => Task.CompletedTask);

        service.Verify(s => s.ReleaseAnonymousConnectionAsync(It.IsAny<string>()), Times.Never);
    }

    // ---------- 回归：登录用户那条路不变 ----------

    [Fact]
    public async Task LoggedInConnection_StillGoesThroughTheUserKeyedChecks()
    {
        var userId = Guid.NewGuid();
        var service = new Mock<IRateLimitService>();
        service.Setup(s => s.IsUserBannedAsync(userId)).ReturnsAsync(false);
        service.Setup(s => s.CheckConnectionLimitAsync(userId)).ReturnsAsync(true);
        var filter = CreateFilter(service.Object, new RateLimitOptions());
        using var hub = new TestHub();

        await filter.OnConnectedAsync(Lifetime(hub, LoggedIn(userId)), _ => Task.CompletedTask);

        service.Verify(s => s.CheckConnectionLimitAsync(userId), Times.Once);
        service.Verify(s => s.TryAcquireAnonymousConnectionAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LoggedInInvocation_StillRecordsAgainstTheUser()
    {
        var userId = Guid.NewGuid();
        var service = new Mock<IRateLimitService>();
        service.Setup(s => s.IsUserBannedAsync(userId)).ReturnsAsync(false);
        service.Setup(s => s.CheckMessageRateLimitAsync(userId)).ReturnsAsync(true);
        var filter = CreateFilter(service.Object, new RateLimitOptions());
        using var hub = new TestHub();

        await filter.InvokeMethodAsync(
            Invocation(hub, LoggedIn(userId)),
            _ => ValueTask.FromResult<object?>(null));

        service.Verify(s => s.RecordMessageAsync(userId), Times.Once);
        service.Verify(s => s.RecordAnonymousMessageAsync(It.IsAny<string>()), Times.Never);
    }
}
