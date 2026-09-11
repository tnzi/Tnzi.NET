using Tnzi.SignalR.Controllers;
using Tnzi.SignalR.Tests.TestDoubles;

namespace Tnzi.SignalR.Tests.Controllers;

/// <summary>
/// <c>DELETE admin/signalr/users/{id}/connections</c> 必须名副其实。
///
/// ★ 权限码描述写的是 "force-disconnects a user's realtime connections"，
/// 而实现只清了 <see cref="IConnectionManager"/> 的登记。那不会动到底层传输：
/// 被"强制下线"的客户端照常连着、照常收广播，只是从管理界面消失了 —— 同时连接计数
/// 被清零，同一用户可以在那些还活着的连接之上再开满一整份配额。
/// </summary>
public class SignalRAdminDisconnectTests
{
    private static (DefaultSignalRAdminController Controller, HubConnectionAborter Aborter, Mock<IConnectionManager> Manager)
        Create(Guid userId, params string[] connectionIds)
    {
        var manager = new Mock<IConnectionManager>();
        manager.Setup(m => m.GetUserConnectionsAsync(userId))
            .ReturnsAsync(connectionIds.AsEnumerable());

        var aborter = new HubConnectionAborter(Mock.Of<ILogger<HubConnectionAborter>>());
        return (new DefaultSignalRAdminController(manager.Object, aborter), aborter, manager);
    }

    [Fact]
    public async Task DisconnectUser_AbortsTheLiveTransports()
    {
        var userId = Guid.NewGuid();
        var (controller, aborter, _) = Create(userId, "conn-1", "conn-2");
        var first = new FakeHubCallerContext("conn-1");
        var second = new FakeHubCallerContext("conn-2");
        aborter.Register("conn-1", first);
        aborter.Register("conn-2", second);

        await controller.DisconnectUser(userId);

        first.AbortCount.ShouldBe(1);
        second.AbortCount.ShouldBe(1);
    }

    [Fact]
    public async Task DisconnectUser_AlsoClearsTheRegistry()
    {
        var userId = Guid.NewGuid();
        var (controller, _, manager) = Create(userId, "conn-1");

        await controller.DisconnectUser(userId);

        manager.Verify(m => m.RemoveUserConnectionsAsync(userId), Times.Once);
    }

    /// <summary>
    /// 别的用户的连接不受影响 —— 中断范围来自该用户自己的连接列表。
    /// </summary>
    [Fact]
    public async Task DisconnectUser_LeavesOtherUsersConnectionsAlone()
    {
        var userId = Guid.NewGuid();
        var (controller, aborter, _) = Create(userId, "conn-mine");
        var mine = new FakeHubCallerContext("conn-mine");
        var theirs = new FakeHubCallerContext("conn-theirs");
        aborter.Register("conn-mine", mine);
        aborter.Register("conn-theirs", theirs);

        await controller.DisconnectUser(userId);

        mine.AbortCount.ShouldBe(1);
        theirs.AbortCount.ShouldBe(0);
    }

    /// <summary>
    /// 中断器缺席（消费方沿用旧的服务图）时仍要清登记，不能整条端点崩掉。
    /// </summary>
    [Fact]
    public async Task DisconnectUser_StillClearsTheRegistryWithoutAnAborter()
    {
        var userId = Guid.NewGuid();
        var manager = new Mock<IConnectionManager>();
        manager.Setup(m => m.GetUserConnectionsAsync(userId)).ReturnsAsync(["conn-1"]);
        var controller = new DefaultSignalRAdminController(manager.Object, connectionAborter: null);

        var result = await controller.DisconnectUser(userId);

        result.Success.ShouldBeTrue();
        manager.Verify(m => m.RemoveUserConnectionsAsync(userId), Times.Once);
    }
}
