using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.SignalR.Hubs;
using Tnzi.SignalR.Metadata;
using Tnzi.SignalR.Tests.TestDoubles;

namespace Tnzi.SignalR.Tests.Hubs;

/// <summary>
/// <see cref="TnziHub"/> 连接两端的登记：框架组进连接管理（管理端看得见），
/// 以及建立连接中途失败时的撤销（SignalR 不会再调断开回调）。
/// </summary>
public class TnziHubConnectLifecycleTests
{
    private sealed class PlainHub : TnziHub
    {
    }

    /// <summary>匿名也能用组帮助方法的 Hub（消费方的公开频道是这个形状）。</summary>
    private sealed class LobbyHub : TnziHub
    {
        public Task JoinLobby() => AddToGroupAsync("lobby");
    }

    private sealed class GroupManagerFailingOn(string failingGroup) : IGroupManager
    {
        public List<string> Added { get; } = [];
        public List<string> Removed { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            if (groupName == failingGroup)
            {
                throw new InvalidOperationException("backplane unavailable");
            }

            Added.Add(groupName);
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removed.Add(groupName);
            return Task.CompletedTask;
        }
    }

    private static ClaimsPrincipal User(Guid userId, Guid? tenantId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (tenantId.HasValue)
        {
            claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    /// <summary>
    /// ★ 回归：框架的 User_ / Tenant_ 组此前直接 <c>Groups.AddToGroupAsync</c> 加入、不经连接管理登记，
    /// 监控页的「组」KPI 恒为 0、每行组列为空、<c>admin/signalr/groups/User_{id}/connections</c> 恒返回空。
    /// </summary>
    [Fact]
    public async Task FrameworkGroups_AreTrackedInTheConnectionManager_AndClearedOnDisconnect()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var manager = new ConnectionManager(NullLogger<ConnectionManager>.Instance);
        using var hub = new PlainHub();
        hub.Context = new FakeHubCallerContext("conn-1", User(userId, tenantId),
            new ServiceCollection().AddSingleton<IConnectionManager>(manager).BuildServiceProvider());
        hub.Groups = new GroupManagerFailingOn(failingGroup: "none");

        await hub.OnConnectedAsync();

        (await manager.GetConnectionGroupsAsync("conn-1")).ShouldBe(
            [HubGroupNames.ForUser(userId), HubGroupNames.ForTenant(tenantId)], ignoreOrder: true);
        (await manager.GetGroupConnectionsAsync(HubGroupNames.ForUser(userId))).ShouldBe(["conn-1"]);

        await hub.OnDisconnectedAsync(null);

        (await manager.GetConnectionGroupsAsync("conn-1")).ShouldBeEmpty();
        (await manager.GetGroupConnectionsAsync(HubGroupNames.ForUser(userId))).ShouldBeEmpty();
    }

    /// <summary>
    /// ★ 回归：加组失败（backplane 的 Redis 故障）时 SignalR 关闭连接、不再调用断开回调，
    /// 此前中断表登记与已加入的用户组永远留着。
    /// </summary>
    [Fact]
    public async Task FailureWhileJoiningGroups_UndoesWhatWasRegistered_AndRethrows()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var aborter = new Mock<IHubConnectionAborter>();
        var manager = new Mock<IConnectionManager>();
        var groups = new GroupManagerFailingOn(HubGroupNames.ForTenant(tenantId));
        using var hub = new PlainHub();
        hub.Context = new FakeHubCallerContext("conn-1", User(userId, tenantId),
            new ServiceCollection().AddSingleton(aborter.Object).AddSingleton(manager.Object).BuildServiceProvider());
        hub.Groups = groups;

        await Should.ThrowAsync<InvalidOperationException>(() => hub.OnConnectedAsync());

        aborter.Verify(a => a.Register("conn-1", hub.Context), Times.Once);
        aborter.Verify(a => a.Unregister("conn-1"), Times.Once);
        groups.Removed.ShouldBe([HubGroupNames.ForUser(userId)]);
        manager.Verify(m => m.AddConnectionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<ConnectionMetadata>()), Times.Never);
    }

    /// <summary>
    /// 匿名连接经组帮助方法登记的组：没有用户 id，<c>RemoveConnectionAsync</c> 清不到，断开时要单独清。
    /// </summary>
    [Fact]
    public async Task AnonymousGroupRegistrations_AreClearedOnDisconnect()
    {
        var manager = new ConnectionManager(NullLogger<ConnectionManager>.Instance);
        using var hub = new LobbyHub();
        hub.Context = new FakeHubCallerContext("anon-1", new ClaimsPrincipal(new ClaimsIdentity()),
            new ServiceCollection().AddSingleton<IConnectionManager>(manager).BuildServiceProvider());
        hub.Groups = new GroupManagerFailingOn(failingGroup: "none");

        await hub.OnConnectedAsync();
        await hub.JoinLobby();
        (await manager.GetGroupConnectionsAsync("lobby")).ShouldBe(["anon-1"]);

        await hub.OnDisconnectedAsync(null);

        (await manager.GetGroupConnectionsAsync("lobby")).ShouldBeEmpty();
        (await manager.GetConnectionGroupsAsync("anon-1")).ShouldBeEmpty();
    }
}
