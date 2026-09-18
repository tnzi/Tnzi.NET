using System.Linq.Expressions;
using Moq;
using Tnzi.Identity.Services;

namespace Tnzi.Identity.Presence.Tests;

/// <summary>
/// PresenceService.ResolveEffectiveAsync（连接状态 + auto-away 综合）与 SetStatus 隐身门控的单元测试。
/// ReportActivity / 连接事件驱动的写路径（真实 UoW/DbContext）目前没有覆盖。
/// </summary>
public class PresenceServiceTests
{
    /// <summary>不裁剪的范围：全部 id 原样放行（多租户未开启 / 无已认证主体）。</summary>
    private static IUserTenantScopeProvider Unrestricted()
    {
        var scope = new Mock<IUserTenantScopeProvider>();
        scope.Setup(s => s.FilterAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.Distinct().ToList());
        return scope.Object;
    }

    /// <summary>裁剪到给定 id 集合的范围（多租户开启、当前租户已知）。</summary>
    private static IUserTenantScopeProvider RestrictedTo(params Guid[] inScope)
    {
        var scope = new Mock<IUserTenantScopeProvider>();
        scope.Setup(s => s.FilterAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.Where(inScope.Contains).Distinct().ToList());
        return scope.Object;
    }

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
        bool autoAwayEnabled = true,
        IUserTenantScopeProvider? scope = null)
    {
        var sp = new Mock<IServiceProvider>();
        var presenceRepo = new Mock<IRepository<UserPresence, Guid>>();
        presenceRepo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<UserPresence, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<UserPresence, bool>> p, CancellationToken _) =>
                (presences ?? new()).Where(p.Compile()).ToList());
        return new PresenceService(
            sp.Object, presenceRepo.Object, Options(allowInvisible, autoAwayEnabled), scope ?? Unrestricted(),
            connectionManager: conn?.Object);
    }

    /// <summary>
    /// ★ 多租户下别家租户的 id 从结果里<b>省略</b>（不是回 Offline，让调用方分得清「不在目录里」与「离线」）。
    /// 此前：对方的 Invisible 行被租户过滤器藏掉 → 走「查不到行默认 Online」→ 连接管理器不分租户答 true →
    /// 对别的租户显示为在线；越是想隐身的人越被暴露，且 GET /presence?userIds= 成了跨租户在线探针。
    /// 连接管理器也只对范围内的 id 被问到。
    /// </summary>
    [Fact]
    public async Task ResolveEffective_ForeignTenantUser_IsOmitted_AndNeverProbed()
    {
        var mine = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var conn = new Mock<IConnectionManager>();
        conn.Setup(c => c.IsUserOnlineAsync(It.IsAny<Guid>())).ReturnsAsync(true);
        // 别家租户的 Invisible 行对当前租户不可见（全局过滤器），仓储里只有自己人的行。
        var svc = Build(conn, new() { new UserPresence { UserId = mine, Status = UserPresenceStatus.Online } }, scope: RestrictedTo(mine));

        var r = await svc.ResolveEffectiveAsync(new[] { mine, foreign });

        r.Select(p => p.UserId).ShouldBe(new[] { mine });
        r.Single().Status.ShouldBe(UserPresenceStatus.Online);
        conn.Verify(c => c.IsUserOnlineAsync(foreign), Times.Never);
    }

    /// <summary>对照组：同租户里查不到行 + 有连接 → Online 的既定设计不变（首次连上、后台处理器尚未 upsert 时也要显示在线）。</summary>
    [Fact]
    public async Task ResolveEffective_SameTenant_NoRecord_Connected_StillOnline()
    {
        var uid = Guid.NewGuid();
        var conn = new Mock<IConnectionManager>();
        conn.Setup(c => c.IsUserOnlineAsync(uid)).ReturnsAsync(true);
        var svc = Build(conn, scope: RestrictedTo(uid));

        var r = await svc.ResolveEffectiveAsync(new[] { uid });

        r.Single().Status.ShouldBe(UserPresenceStatus.Online);
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
