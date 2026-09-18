using Microsoft.Extensions.Caching.Distributed;
using Tnzi.Identity.Options;
using Tnzi.Identity.Services;
using Tnzi.MultiTenancy;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 多租户开启时，会话管理端按当前租户裁剪。
///
/// ★ <c>UserSession</c> 没有 <c>TenantId</c>、不是 <c>IMultiTenant</c>，全局过滤器管不到它；
/// 而它的管理端点（按用户读会话、踢掉某人全部会话、按会话 id 撤销、全局列表 / 统计 / 活跃用户）
/// 此前一条都不看目标用户归哪个租户 —— 与 <c>UserService</c> 修过的那条是同一个缺陷，隔了一个控制器。
/// 口径与 <c>UserTenantScopeIntegrationTests</c> 相同：越界一律 404，列表按用户表的租户裁剪，
/// 没有租户上下文的全局管理员不裁剪。
///
/// ★ 会话服务同时被登录链路调用（多端登录顶替、登出、绑定失配撤销），那时没有已认证主体，
/// 而租户上下文可能来自 header 或 <c>DefaultTenantId</c>；「没有主体 = 系统流程 = 不裁剪」
/// 是让这两类调用共用同一批方法的前提，最后一条用例钉住它。
/// </summary>
public class SessionTenantScopeIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly Mock<ICurrentTenant> _currentTenant = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly DatabaseSessionService _sessions;

    private Guid? _tenantId;
    private bool _authenticated = true;

    public SessionTenantScopeIntegrationTests()
        : base(configureServices: services => services.Configure<MultiTenancyOptions>(o => o.Enabled = true))
    {
        _currentTenant.Setup(t => t.Id).Returns(() => _tenantId);
        _currentUser.Setup(u => u.IsAuthenticated).Returns(() => _authenticated);
        _currentUser.Setup(u => u.TenantId).Returns(() => _tenantId);

        var scope = new UserTenantScopeProvider(
            CreateRepository<User>(),
            _currentTenant.Object,
            _currentUser.Object,
            Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true }));
        _sessions = new DatabaseSessionService(CreateRepository<UserSession>(), ServiceProvider, scope);
        _scope = scope;
    }

    private readonly UserTenantScopeProvider _scope;

    /// <summary>
    /// Redis 模式的会话服务，开着数据库审计表：清扫在那条路径上落到同一张 <c>UserSession</c> 表，
    /// 裁剪的口径必须与 <see cref="DatabaseSessionService"/> 逐字相同。缓存本身与清扫无关，给个空实现即可。
    /// </summary>
    private DistributedSessionService CreateDistributedSessions() => new(
        new Mock<IDistributedCache>().Object,
        Microsoft.Extensions.Options.Options.Create(new SessionOptions { KeepDatabaseAuditLog = true }),
        ServiceProvider,
        _scope,
        CreateRepository<UserSession>());

    private void ActAs(Guid adminId, Guid? tenantId)
    {
        _currentUser.Setup(u => u.Id).Returns(adminId);
        _tenantId = tenantId;
        _authenticated = true;
    }

    /// <summary>登录链路：没有已认证主体，但租户上下文已经由 header / DefaultTenantId 建立。</summary>
    private void ActAsSystemInTenant(Guid tenantId)
    {
        _currentUser.Setup(u => u.Id).Returns((Guid?)null);
        _tenantId = tenantId;
        _authenticated = false;
    }

    private async Task<User> CreateTenantUserAsync(Guid? tenantId, string email)
    {
        var user = await CreateUserAsync(email: email);
        user.TenantId = tenantId;
        await SaveChangesAsync();
        return user;
    }

    private async Task<UserSession> AddSessionAsync(User user, string device, TimeSpan? inactiveFor = null)
    {
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DeviceInfo = device,
            LastActivityTime = DateTime.UtcNow - (inactiveFor ?? TimeSpan.Zero),
            CreationTime = DateTime.UtcNow,
            IsRevoked = false
        };
        DbContext.UserSessions.Add(session);
        await SaveChangesAsync();
        return session;
    }

    private async Task<(User AdminA, User UserA, User UserB, UserSession SessionA, UserSession SessionB)> SeedAsync()
    {
        var adminA = await CreateTenantUserAsync(TenantA, "admin-a@example.com");
        var userA = await CreateTenantUserAsync(TenantA, "user-a@example.com");
        var userB = await CreateTenantUserAsync(TenantB, "user-b@example.com");
        var sessionA = await AddSessionAsync(userA, "DeviceA");
        var sessionB = await AddSessionAsync(userB, "DeviceB");
        DbContext.ChangeTracker.Clear();
        return (adminA, userA, userB, sessionA, sessionB);
    }

    private async Task<UserSession> ReloadAsync(Guid sessionId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.UserSessions.SingleAsync(s => s.Id == sessionId);
    }

    [Fact]
    public async Task TenantAdmin_GetUserSessions_OnOtherTenantUser_Returns404()
    {
        var (adminA, _, userB, _, _) = await SeedAsync();
        ActAs(adminA.Id, TenantA);

        var result = await _sessions.GetUserSessionsAsync(userB.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task TenantAdmin_RevokeAll_OnOtherTenantUser_Returns404_AndRevokesNothing()
    {
        var (adminA, _, userB, _, sessionB) = await SeedAsync();
        ActAs(adminA.Id, TenantA);

        var result = await _sessions.RevokeAllSessionsAsync(userB.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        Assert.False((await ReloadAsync(sessionB.Id)).IsRevoked);
    }

    [Fact]
    public async Task TenantAdmin_RevokeSession_OfOtherTenantUser_Returns404_AndSessionStaysActive()
    {
        var (adminA, _, _, _, sessionB) = await SeedAsync();
        ActAs(adminA.Id, TenantA);

        var result = await _sessions.RevokeSessionAsync(sessionB.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        Assert.False((await ReloadAsync(sessionB.Id)).IsRevoked);
    }

    [Fact]
    public async Task TenantAdmin_GlobalList_Statistics_And_ActiveUsers_CoverOnlyOwnTenant()
    {
        var (adminA, userA, userB, sessionA, sessionB) = await SeedAsync();
        ActAs(adminA.Id, TenantA);

        var list = await _sessions.GetSessionsAsync(new SessionQueryDto { PageIndex = 1, PageSize = 50 });
        Assert.True(list.Succeeded, list.Message);
        Assert.Contains(list.Data!.Items, s => s.Id == sessionA.Id);
        Assert.DoesNotContain(list.Data!.Items, s => s.Id == sessionB.Id);
        Assert.Equal(1, list.Data!.TotalCount);

        var byForeignUser = await _sessions.GetSessionsAsync(new SessionQueryDto { UserId = userB.Id, PageIndex = 1, PageSize = 50 });
        Assert.True(byForeignUser.Succeeded, byForeignUser.Message);
        Assert.Empty(byForeignUser.Data!.Items);

        var statistics = await _sessions.GetSessionStatisticsAsync();
        Assert.True(statistics.Succeeded, statistics.Message);
        Assert.Equal(1, statistics.Data!.ActiveSessionCount);
        Assert.Equal(1, statistics.Data!.OnlineUserCount);
        Assert.DoesNotContain(statistics.Data!.TopDevices, d => d.DeviceInfo == "DeviceB");

        var active = await _sessions.GetActiveUsersAsync();
        Assert.True(active.Succeeded, active.Message);
        Assert.Contains(active.Data!, u => u.UserId == userA.Id);
        Assert.DoesNotContain(active.Data!, u => u.UserId == userB.Id);
    }

    [Fact]
    public async Task TenantAdmin_CanStillManage_OwnTenantUser()
    {
        var (adminA, userA, _, sessionA, _) = await SeedAsync();
        ActAs(adminA.Id, TenantA);

        var listed = await _sessions.GetUserSessionsAsync(userA.Id);
        Assert.True(listed.Succeeded, listed.Message);
        Assert.Single(listed.Data!);

        var revoked = await _sessions.RevokeSessionAsync(sessionA.Id);
        Assert.True(revoked.Succeeded, revoked.Message);
        Assert.True((await ReloadAsync(sessionA.Id)).IsRevoked);
    }

    /// <summary>
    /// 软删的账号仍归原租户的管理员管：它的会话还在，按过滤器隐掉会让「同租户、已删除」与「别家租户」长得一样。
    /// </summary>
    [Fact]
    public async Task TenantAdmin_CanStillReadSessions_OfASoftDeletedUserInOwnTenant()
    {
        var (adminA, userA, _, _, _) = await SeedAsync();
        var row = await DbContext.Users.SingleAsync(u => u.Id == userA.Id);
        row.IsDeleted = true;
        await SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        ActAs(adminA.Id, TenantA);

        var listed = await _sessions.GetUserSessionsAsync(userA.Id, includeRevoked: true);

        Assert.True(listed.Succeeded, listed.Message);
        Assert.Single(listed.Data!);
        var list = await _sessions.GetSessionsAsync(new SessionQueryDto { PageIndex = 1, PageSize = 50, IncludeRevoked = true });
        Assert.Contains(list.Data!.Items, s => s.UserId == userA.Id);
    }

    [Fact]
    public async Task GlobalAdmin_WithNoTenantContext_SeesAndRevokesEveryTenant()
    {
        var (_, _, userB, sessionA, sessionB) = await SeedAsync();
        ActAs(Guid.NewGuid(), null);

        var list = await _sessions.GetSessionsAsync(new SessionQueryDto { PageIndex = 1, PageSize = 50 });
        Assert.True(list.Succeeded, list.Message);
        Assert.Contains(list.Data!.Items, s => s.Id == sessionA.Id);
        Assert.Contains(list.Data!.Items, s => s.Id == sessionB.Id);

        var revoked = await _sessions.RevokeAllSessionsAsync(userB.Id);
        Assert.True(revoked.Succeeded, revoked.Message);
        Assert.True((await ReloadAsync(sessionB.Id)).IsRevoked);
    }

    /// <summary>
    /// <c>FilterAsync</c>：一次 IN 查询把一批 id 收窄到范围内（同租户 + 本人）；不裁剪时原样返回且不查库。
    /// Presence 的批量解析靠它把别家租户的 id 从结果里省掉。
    /// </summary>
    [Fact]
    public async Task FilterAsync_KeepsOwnTenantAndSelf_DropsForeignAndUnknown()
    {
        var (adminA, userA, userB, _, _) = await SeedAsync();
        var global = await CreateTenantUserAsync(null, "global@example.com");
        DbContext.ChangeTracker.Clear();
        ActAs(adminA.Id, TenantA);
        var scope = new UserTenantScopeProvider(
            CreateRepository<User>(), _currentTenant.Object, _currentUser.Object,
            Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true }));

        var kept = await scope.FilterAsync([adminA.Id, userA.Id, userB.Id, global.Id, Guid.NewGuid()]);

        Assert.Equal(new HashSet<Guid> { adminA.Id, userA.Id }, kept.ToHashSet());

        ActAs(adminA.Id, null);
        var unrestricted = await scope.FilterAsync([userB.Id, Guid.NewGuid()]);
        Assert.Equal(2, unrestricted.Count);
    }

    /// <summary>
    /// 登录链路（多端登录顶替、登出）在没有已认证主体的请求里调用同一批方法，
    /// 而租户上下文可能是 header 给的、与正在登录的人不同 —— 那时不能裁剪，否则顶替静默失效。
    /// </summary>
    [Fact]
    public async Task SystemFlow_WithoutPrincipal_IsNotScoped_EvenInsideAForeignTenantContext()
    {
        var (_, _, userB, _, sessionB) = await SeedAsync();
        ActAsSystemInTenant(TenantA);

        var listed = await _sessions.GetUserSessionsAsync(userB.Id);
        Assert.True(listed.Succeeded, listed.Message);
        Assert.Single(listed.Data!);

        var revoked = await _sessions.RevokeAllSessionsAsync(userB.Id);
        Assert.True(revoked.Succeeded, revoked.Message);
        Assert.True((await ReloadAsync(sessionB.Id)).IsRevoked);
    }

    /// <summary>
    /// ★ 清扫失活会话（<c>POST admin/sessions/clean-expired</c>）走的是同一个撤销出口，但它不按会话 id 也不按用户 id，
    /// 而是按「谁不活跃了」整表扫 —— 单条 / 该用户全部两条修好之后它仍是全租户的：
    /// 租户 A 的管理员传 <c>inactiveMinutes=0</c>，所有租户所有未撤销会话一次翻成已撤销，绑定的刷新令牌随之全删。
    /// 裁剪与列表 / 统计同源：只扫本租户账号的会话，返回的 id 里也不含别家的（出口拿它去删令牌）。
    /// </summary>
    [Fact]
    public async Task TenantAdmin_CleanInactive_SweepsOnlyOwnTenant()
    {
        var (adminA, userA, userB, _, _) = await SeedAsync();
        var staleA = await AddSessionAsync(userA, "StaleA", inactiveFor: TimeSpan.FromHours(2));
        var staleB = await AddSessionAsync(userB, "StaleB", inactiveFor: TimeSpan.FromHours(2));
        DbContext.ChangeTracker.Clear();
        ActAs(adminA.Id, TenantA);

        var cleaned = await _sessions.CleanInactiveSessionsAsync(TimeSpan.FromMinutes(30));

        Assert.True(cleaned.Succeeded, cleaned.Message);
        Assert.Equal([staleA.Id], cleaned.Data!);
        Assert.True((await ReloadAsync(staleA.Id)).IsRevoked);
        Assert.False((await ReloadAsync(staleB.Id)).IsRevoked);
    }

    /// <summary>同上，Redis 模式 + 数据库审计表那条路径。</summary>
    [Fact]
    public async Task TenantAdmin_CleanInactive_ViaDistributedService_SweepsOnlyOwnTenant()
    {
        var (adminA, userA, userB, _, _) = await SeedAsync();
        var staleA = await AddSessionAsync(userA, "StaleA", inactiveFor: TimeSpan.FromHours(2));
        var staleB = await AddSessionAsync(userB, "StaleB", inactiveFor: TimeSpan.FromHours(2));
        DbContext.ChangeTracker.Clear();
        ActAs(adminA.Id, TenantA);

        var cleaned = await CreateDistributedSessions().CleanInactiveSessionsAsync(TimeSpan.FromMinutes(30));

        Assert.True(cleaned.Succeeded, cleaned.Message);
        Assert.Equal([staleA.Id], cleaned.Data!);
        Assert.True((await ReloadAsync(staleA.Id)).IsRevoked);
        Assert.False((await ReloadAsync(staleB.Id)).IsRevoked);
    }

    /// <summary>
    /// 后台维护任务（<c>SessionMaintenanceBackgroundService</c>）在没有主体的作用域里跑同一条清扫，
    /// 那时必须是全量的 —— 裁剪只对已认证的管理端生效。
    /// </summary>
    [Fact]
    public async Task SystemFlow_CleanInactive_IsNotScoped()
    {
        var (_, userA, userB, _, _) = await SeedAsync();
        var staleA = await AddSessionAsync(userA, "StaleA", inactiveFor: TimeSpan.FromHours(2));
        var staleB = await AddSessionAsync(userB, "StaleB", inactiveFor: TimeSpan.FromHours(2));
        DbContext.ChangeTracker.Clear();
        ActAsSystemInTenant(TenantA);

        var cleaned = await _sessions.CleanInactiveSessionsAsync(TimeSpan.FromMinutes(30));

        Assert.True(cleaned.Succeeded, cleaned.Message);
        Assert.Equal(new HashSet<Guid> { staleA.Id, staleB.Id }, cleaned.Data!.ToHashSet());
        Assert.True((await ReloadAsync(staleB.Id)).IsRevoked);
    }
}
