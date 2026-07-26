using System.Linq.Expressions;
using Moq;

namespace Tnzi.Identity.Presence.Tests;

/// <summary>
/// PresenceService.ResolveEffectiveAsync（连接状态 + auto-away 综合）与 SetStatus 隐身门控的单元测试。
/// ReportActivity / 连接事件驱动的写路径由集成测试（真实 UoW/DbContext）覆盖。
/// </summary>
public class PresenceServiceTests
{
    private static IOptionsSnapshot<PresenceOptions> Options(bool allowInvisible = true, bool autoAwayEnabled = true)
    {
        var opt = new Mock<IOptionsSnapshot<PresenceOptions>>();
        opt.Setup(o => o.Value).Returns(new PresenceOptions { AllowInvisible = allowInvisible, AutoAwayEnabled = autoAwayEnabled });
        return opt.Object;
    }

    private static PresenceService Build(
        Mock<IConnectionManager>? conn = null,
        List<UserPresence>? presences = null,
        bool allowInvisible = true,
        bool autoAwayEnabled = true)
    {
        var sp = new Mock<IServiceProvider>();
        var presenceRepo = new Mock<IRepository<UserPresence, Guid>>();
        presenceRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<UserPresence, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<UserPresence, bool>> p, CancellationToken _) =>
                (presences ?? new()).Where(p.Compile()).ToList());
        return new PresenceService(sp.Object, presenceRepo.Object, Options(allowInvisible, autoAwayEnabled), connectionManager: conn?.Object);
    }

    [Fact]
    public async Task ResolveEffective_NoRecord_ConnectedManager_Online()
    {
        var conn = new Mock<IConnectionManager>();
        conn.Setup(c => c.IsUserOnlineAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        var svc = Build(conn);
        var r = await svc.ResolveEffectiveAsync(new[] { Guid.NewGuid() });
        r.Single().Status.ShouldBe(UserPresenceStatus.Online);
    }

    [Fact]
    public async Task ResolveEffective_OnlineIntent_NotConnected_Offline()
    {
        var uid = Guid.NewGuid();
        var conn = new Mock<IConnectionManager>();
        conn.Setup(c => c.IsUserOnlineAsync(uid)).ReturnsAsync(false);
        var svc = Build(conn, new() { new UserPresence { UserId = uid, Status = UserPresenceStatus.Online, LastSeenAt = DateTime.UtcNow } });
        var r = await svc.ResolveEffectiveAsync(new[] { uid });
        r.Single().Status.ShouldBe(UserPresenceStatus.Offline);
        r.Single().LastSeenAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ResolveEffective_Invisible_ShownOffline_EvenIfConnected()
    {
        var uid = Guid.NewGuid();
        var conn = new Mock<IConnectionManager>();
        conn.Setup(c => c.IsUserOnlineAsync(uid)).ReturnsAsync(true);
        var svc = Build(conn, new() { new UserPresence { UserId = uid, Status = UserPresenceStatus.Invisible } });
        var r = await svc.ResolveEffectiveAsync(new[] { uid });
        r.Single().Status.ShouldBe(UserPresenceStatus.Offline);
    }

    [Fact]
    public async Task SetStatus_Invisible_Should_Fail_403_When_Invisible_Disabled()
    {
        // AllowInvisible=false → 服务端强制拒绝隐身意图（前端已隐藏选项，此为兜底）。
        // 该拒绝在读取 CurrentUser 之前短路，故无需完整鉴权上下文。
        var svc = Build(allowInvisible: false);
        var r = await svc.SetStatusAsync(UserPresenceStatus.Invisible);
        r.Succeeded.ShouldBeFalse();
        r.Code.ShouldBe(403);
    }

    [Fact]
    public async Task ResolveEffective_Invisible_Disabled_Resolves_As_Online_When_Connected()
    {
        var uid = Guid.NewGuid();
        var conn = new Mock<IConnectionManager>();
        conn.Setup(c => c.IsUserOnlineAsync(uid)).ReturnsAsync(true);
        var svc = Build(conn, new() { new UserPresence { UserId = uid, Status = UserPresenceStatus.Invisible } }, allowInvisible: false);
        var r = await svc.ResolveEffectiveAsync(new[] { uid });
        r.Single().Status.ShouldBe(UserPresenceStatus.Online);
    }

    [Fact]
    public async Task ResolveEffective_Busy_Connected_Busy()
    {
        var uid = Guid.NewGuid();
        var conn = new Mock<IConnectionManager>();
        conn.Setup(c => c.IsUserOnlineAsync(uid)).ReturnsAsync(true);
        var svc = Build(conn, new() { new UserPresence { UserId = uid, Status = UserPresenceStatus.Busy } });
        var r = await svc.ResolveEffectiveAsync(new[] { uid });
        r.Single().Status.ShouldBe(UserPresenceStatus.Busy);
    }

    [Fact]
    public async Task ResolveEffective_NoConnectionManager_ManualOnlyMode()
    {
        var uid = Guid.NewGuid();
        var svc = Build(conn: null, new() { new UserPresence { UserId = uid, Status = UserPresenceStatus.Away } });
        var r = await svc.ResolveEffectiveAsync(new[] { uid });
        r.Single().Status.ShouldBe(UserPresenceStatus.Away); // 无 SignalR → 手动值原样
    }

    [Fact]
    public async Task ResolveEffective_Idle_Online_Connected_Resolves_Away()
    {
        // auto-away：客户端上报空闲（IsAutoAway=true）+ 意图 Online + 有连接 → 有效 Away。
        var uid = Guid.NewGuid();
        var conn = new Mock<IConnectionManager>();
        conn.Setup(c => c.IsUserOnlineAsync(uid)).ReturnsAsync(true);
        var svc = Build(conn, new() { new UserPresence { UserId = uid, Status = UserPresenceStatus.Online, IsAutoAway = true } });
        var r = await svc.ResolveEffectiveAsync(new[] { uid });
        r.Single().Status.ShouldBe(UserPresenceStatus.Away);
    }

    [Fact]
    public async Task ResolveEffective_Idle_But_AutoAway_Disabled_Stays_Online()
    {
        var uid = Guid.NewGuid();
        var conn = new Mock<IConnectionManager>();
        conn.Setup(c => c.IsUserOnlineAsync(uid)).ReturnsAsync(true);
        var svc = Build(conn, new() { new UserPresence { UserId = uid, Status = UserPresenceStatus.Online, IsAutoAway = true } },
            autoAwayEnabled: false);
        var r = await svc.ResolveEffectiveAsync(new[] { uid });
        r.Single().Status.ShouldBe(UserPresenceStatus.Online);
    }
}
