using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.SignalR.Hubs;
using Tnzi.SignalR.Metadata;
using Tnzi.SignalR.Tests.TestDoubles;

namespace Tnzi.SignalR.Tests.Hubs;

/// <summary>
/// <see cref="TnziHub"/> 的连接追踪。
///
/// ★ 基类有一个无参构造。用它继承（框架自己的 <c>SettingsRealtimeHub</c> 就是
/// <c>public class SettingsRealtimeHub : TnziHub { }</c>）会让 <c>IConnectionManager</c>
/// 为 null，于是这些连接**静默地**不参与任何追踪：不出现在 admin 的三个查询端点里、
/// 不计入 <c>MaxConnectionsPerUser</c>、<c>DELETE admin/signalr/users/{id}/connections</c>
/// 对它们无效。没有报错，没有日志，连接本身完全正常。
///
/// 所以基类必须能在没被注入时**自己**从连接的请求服务里解析出来。
/// </summary>
public class TnziHubConnectionTrackingTests
{
    /// <summary>框架自己的 SettingsRealtimeHub 就是这个形状。</summary>
    private sealed class ParameterlessHub : TnziHub
    {
    }

    private sealed class InjectedHub(IConnectionManager connectionManager) : TnziHub(connectionManager)
    {
    }

    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string GroupName)> Added { get; } = [];
        public List<(string ConnectionId, string GroupName)> Removed { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removed.Add((connectionId, groupName));
            return Task.CompletedTask;
        }
    }

    private static ClaimsPrincipal UserWithId(Guid id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test"));

    private static (FakeHubCallerContext Context, RecordingGroupManager Groups) Wire(
        TnziHub hub,
        Guid userId,
        IConnectionManager? available,
        string connectionId = "conn-1")
    {
        var services = new ServiceCollection();
        if (available != null) services.AddSingleton(available);

        var context = new FakeHubCallerContext(
            connectionId: connectionId,
            user: UserWithId(userId),
            requestServices: services.BuildServiceProvider());
        var groups = new RecordingGroupManager();

        hub.Context = context;
        hub.Groups = groups;
        return (context, groups);
    }

    /// <summary>
    /// ★ 核心：无参构造的 Hub 也必须把连接登记到 <see cref="IConnectionManager"/>。
    /// </summary>
    [Fact]
    public async Task ParameterlessHub_StillTracksTheConnection()
    {
        var manager = new Mock<IConnectionManager>();
        var userId = Guid.NewGuid();
        using var hub = new ParameterlessHub();
        Wire(hub, userId, manager.Object);

        await hub.OnConnectedAsync();

        manager.Verify(
            m => m.AddConnectionAsync(userId, "conn-1", It.IsAny<ConnectionMetadata>()),
            Times.Once);
    }

    [Fact]
    public async Task ParameterlessHub_StillRemovesTheConnectionOnDisconnect()
    {
        var manager = new Mock<IConnectionManager>();
        var userId = Guid.NewGuid();
        using var hub = new ParameterlessHub();
        Wire(hub, userId, manager.Object);

        await hub.OnDisconnectedAsync(exception: null);

        manager.Verify(m => m.RemoveConnectionAsync(userId, "conn-1"), Times.Once);
    }

    /// <summary>
    /// 注入优先：显式传进来的实例必须被用上，不能被解析出来的另一个顶掉。
    /// </summary>
    [Fact]
    public async Task InjectedManagerWins_OverTheOneInRequestServices()
    {
        var injected = new Mock<IConnectionManager>();
        var resolvable = new Mock<IConnectionManager>();
        var userId = Guid.NewGuid();
        using var hub = new InjectedHub(injected.Object);
        Wire(hub, userId, resolvable.Object);

        await hub.OnConnectedAsync();

        injected.Verify(m => m.AddConnectionAsync(userId, "conn-1", It.IsAny<ConnectionMetadata>()), Times.Once);
        resolvable.Verify(
            m => m.AddConnectionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<ConnectionMetadata>()),
            Times.Never);
    }

    /// <summary>
    /// 解析不到时连接生命周期照常走完 —— 追踪是辅助能力，不是连接的前提。
    /// </summary>
    [Fact]
    public async Task ConnectionStillSucceeds_WhenNoManagerCanBeResolved()
    {
        var userId = Guid.NewGuid();
        using var hub = new ParameterlessHub();
        var (_, groups) = Wire(hub, userId, available: null);

        await hub.OnConnectedAsync();

        groups.Added.ShouldContain((("conn-1", $"User_{userId}")));
    }

    /// <summary>
    /// 每个 Hub 实例都是新的，但同一条连接的 <c>Context.Items</c> 是共享的：
    /// 第一次解析的结果存在那里，断开时（请求作用域可能已经不可用）仍然拿得到。
    /// </summary>
    [Fact]
    public async Task ResolvedManagerIsCachedOnTheConnection_SoDisconnectFindsItToo()
    {
        var manager = new Mock<IConnectionManager>();
        var userId = Guid.NewGuid();

        using var connectHub = new ParameterlessHub();
        var (context, _) = Wire(connectHub, userId, manager.Object);
        await connectHub.OnConnectedAsync();

        // 断开时是另一个 Hub 实例，且这一次请求服务里已经解析不到了
        using var disconnectHub = new ParameterlessHub();
        var starved = new FakeHubCallerContext(
            connectionId: "conn-1",
            user: UserWithId(userId),
            requestServices: new ServiceCollection().BuildServiceProvider());
        foreach (var (key, value) in context.Items)
        {
            starved.Items[key] = value;
        }
        disconnectHub.Context = starved;
        disconnectHub.Groups = new RecordingGroupManager();

        await disconnectHub.OnDisconnectedAsync(exception: null);

        manager.Verify(m => m.RemoveConnectionAsync(userId, "conn-1"), Times.Once);
    }

    /// <summary>
    /// 中断表按 connectionId 索引，与登录与否无关 —— 匿名连接也必须是可中断的。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConnectionIsRegisteredWithTheAborter_LoggedInOrNot(bool authenticated)
    {
        var aborter = new Mock<IHubConnectionAborter>();
        using var hub = new ParameterlessHub();
        hub.Context = new FakeHubCallerContext(
            connectionId: "conn-1",
            user: authenticated
                ? UserWithId(Guid.NewGuid())
                : new ClaimsPrincipal(new ClaimsIdentity()),
            requestServices: new ServiceCollection().AddSingleton(aborter.Object).BuildServiceProvider());
        hub.Groups = new RecordingGroupManager();

        await hub.OnConnectedAsync();

        aborter.Verify(a => a.Register("conn-1", hub.Context), Times.Once);
    }

    [Fact]
    public async Task ConnectionIsUnregisteredFromTheAborterOnDisconnect()
    {
        var aborter = new Mock<IHubConnectionAborter>();
        using var hub = new ParameterlessHub();
        hub.Context = new FakeHubCallerContext(
            connectionId: "conn-1",
            user: UserWithId(Guid.NewGuid()),
            requestServices: new ServiceCollection().AddSingleton(aborter.Object).BuildServiceProvider());
        hub.Groups = new RecordingGroupManager();

        await hub.OnDisconnectedAsync(exception: null);

        aborter.Verify(a => a.Unregister("conn-1"), Times.Once);
    }

    [Fact]
    public async Task AnonymousConnection_IsNotTrackedAsAUser()
    {
        var manager = new Mock<IConnectionManager>();
        using var hub = new ParameterlessHub();
        hub.Context = new FakeHubCallerContext(
            user: new ClaimsPrincipal(new ClaimsIdentity()),
            requestServices: new ServiceCollection().AddSingleton(manager.Object).BuildServiceProvider());
        hub.Groups = new RecordingGroupManager();

        await hub.OnConnectedAsync();

        manager.Verify(
            m => m.AddConnectionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<ConnectionMetadata>()),
            Times.Never);
    }

    /// <summary>
    /// ★ 带租户 claim 的连接进 <c>Tenant_{id}</c> 组：「发给整个租户」的推送靠它，而不是 <c>Clients.All</c>
    /// （多租户下 All 会把一家租户的事件送到所有租户）。断开时移出。
    /// </summary>
    [Fact]
    public async Task JoinsTheTenantGroup_WhenThePrincipalCarriesATenantClaim_AndLeavesItOnDisconnect()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        using var hub = new ParameterlessHub();
        var groups = new RecordingGroupManager();
        hub.Context = new FakeHubCallerContext(
            connectionId: "conn-t",
            user: new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim("tenant_id", tenantId.ToString())], "test")),
            requestServices: new ServiceCollection().BuildServiceProvider());
        hub.Groups = groups;

        await hub.OnConnectedAsync();
        await hub.OnDisconnectedAsync(null);

        groups.Added.ShouldContain(("conn-t", HubGroupNames.ForTenant(tenantId)));
        groups.Added.ShouldContain(("conn-t", HubGroupNames.ForUser(userId)));
        groups.Removed.ShouldContain(("conn-t", HubGroupNames.ForTenant(tenantId)));
    }

    /// <summary>没有租户 claim 的连接不进任何租户组 —— 关闭方向：按租户推送时它收不到，而不是收到所有租户的。</summary>
    [Fact]
    public async Task JoinsNoTenantGroup_WithoutATenantClaim()
    {
        var userId = Guid.NewGuid();
        using var hub = new ParameterlessHub();
        var (_, groups) = Wire(hub, userId, available: null);

        await hub.OnConnectedAsync();

        groups.Added.ShouldNotContain(g => g.GroupName.StartsWith(HubGroupNames.TenantGroupPrefix, StringComparison.Ordinal));
    }
}
