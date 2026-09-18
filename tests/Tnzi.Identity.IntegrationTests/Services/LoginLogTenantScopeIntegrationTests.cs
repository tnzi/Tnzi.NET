using Microsoft.Extensions.Options;
using Tnzi.Identity.Services;
using Tnzi.MultiTenancy;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 多租户开启时，登录日志与登录安全的管理端按当前租户裁剪。
///
/// ★ <c>LoginLog</c> 没有 <c>TenantId</c>、不是 <c>IMultiTenant</c>，全局过滤器管不到它；
/// 此前 <c>admin/login-logs</c>（按用户读、按用户统计、不带 userId 的列表、失败尝试、趋势）
/// 与 <c>admin/login-security</c>（最近登录、常用 IP、异常检测、总览、频繁失败）一条都不看目标用户归哪个租户：
/// 租户管理员按 id 就能读到任何租户用户的登录史（IP、UA、时间、失败原因），列表端点返回全部租户的日志。
/// 口径与会话管理端相同：按用户 id 的读越界答 404 / 空，列表与统计按用户表的租户裁剪；
/// 没有归属的日志行（用户名不存在的失败尝试，<c>UserId = null</c>）只有不裁剪的全局管理员看得到。
/// </summary>
public class LoginLogTenantScopeIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly Mock<ICurrentTenant> _currentTenant = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly LoginLogService _logs;
    private readonly LoginSecurityService _security;

    private Guid? _tenantId;
    private bool _authenticated = true;

    public LoginLogTenantScopeIntegrationTests()
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
        _logs = new LoginLogService(CreateRepository<LoginLog>(), LoginLogSenderMock.Object, ServiceProvider, scope);
        _security = new LoginSecurityService(
            ServiceProvider,
            scope,
            ServiceProvider.GetRequiredService<IOptionsMonitor<IdentityOptions>>(),
            CreateRepository<LoginLog>(),
            UserManager);
    }

    private void ActAs(Guid adminId, Guid? tenantId)
    {
        _currentUser.Setup(u => u.Id).Returns(adminId);
        _tenantId = tenantId;
        _authenticated = true;
    }

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

    private async Task AddLogAsync(Guid? userId, string ip, LoginStatus status, DateTime? at = null)
    {
        DbContext.LoginLogs.Add(new LoginLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            UserName = userId?.ToString() ?? "nobody",
            IpAddress = ip,
            UserAgent = "TestAgent",
            Status = status,
            CreationTime = at ?? DateTime.UtcNow
        });
        await SaveChangesAsync();
    }

    private async Task<(User AdminA, User UserA, User UserB)> SeedAsync()
    {
        var adminA = await CreateTenantUserAsync(TenantA, "admin-a@example.com");
        var userA = await CreateTenantUserAsync(TenantA, "user-a@example.com");
        var userB = await CreateTenantUserAsync(TenantB, "user-b@example.com");
        await AddLogAsync(userA.Id, "10.0.0.1", LoginStatus.Success);
        await AddLogAsync(userA.Id, "10.0.0.1", LoginStatus.Failed);
        await AddLogAsync(userB.Id, "10.0.0.2", LoginStatus.Success);
        await AddLogAsync(userB.Id, "10.0.0.2", LoginStatus.Failed);
        await AddLogAsync(userB.Id, "10.0.0.2", LoginStatus.Failed);
        await AddLogAsync(userB.Id, "10.0.0.2", LoginStatus.Failed);
        await AddLogAsync(null, "10.0.0.9", LoginStatus.Failed);
        DbContext.ChangeTracker.Clear();
        return (adminA, userA, userB);
    }

    [Fact]
    public async Task TenantAdmin_PerUserLogReads_OnOtherTenantUser_Return404()
    {
        var (adminA, _, userB) = await SeedAsync();
        ActAs(adminA.Id, TenantA);

        var logs = await _logs.GetUserLoginLogsAsync(userB.Id);
        Assert.False(logs.Succeeded);
        Assert.Equal(404, logs.Code);

        var statistics = await _logs.GetUserStatisticsAsync(userB.Id);
        Assert.False(statistics.Succeeded);
        Assert.Equal(404, statistics.Code);
    }

    [Fact]
    public async Task TenantAdmin_LogList_Statistics_FailedAttempts_And_Trend_CoverOnlyOwnTenant()
    {
        var (adminA, userA, userB) = await SeedAsync();
        ActAs(adminA.Id, TenantA);

        var list = await _logs.GetPagedListAsync(new LoginLogQueryDto { PageIndex = 1, PageSize = 50 });
        Assert.True(list.Succeeded, list.Message);
        Assert.Equal(2, list.Data!.TotalCount);
        Assert.All(list.Data!.Items, l => Assert.Equal(userA.Id, l.UserId));

        var byForeignUser = await _logs.GetPagedListAsync(new LoginLogQueryDto { UserId = userB.Id, PageIndex = 1, PageSize = 50 });
        Assert.True(byForeignUser.Succeeded, byForeignUser.Message);
        Assert.Equal(0, byForeignUser.Data!.TotalCount);

        var statistics = await _logs.GetStatisticsAsync();
        Assert.True(statistics.Succeeded, statistics.Message);
        Assert.Equal(2, statistics.Data!.TotalLogins);

        var failed = await _logs.GetFailedAttemptsAsync();
        Assert.True(failed.Succeeded, failed.Message);
        Assert.Single(failed.Data!);
        Assert.Equal(userA.Id, failed.Data!.Single().UserId);

        var trend = await _logs.GetLoginTrendAsync(DateTime.UtcNow.Date.AddDays(-1), DateTime.UtcNow.Date.AddDays(1));
        Assert.True(trend.Succeeded, trend.Message);
        Assert.Equal(2, trend.Data!.Sum(t => t.TotalLogins));
    }

    [Fact]
    public async Task TenantAdmin_LoginSecurityPerUserReads_OnOtherTenantUser_ReturnNothing()
    {
        var (adminA, _, userB) = await SeedAsync();
        ActAs(adminA.Id, TenantA);

        Assert.Empty(await _security.GetRecentLoginsAsync(userB.Id));
        Assert.Empty(await _security.GetFrequentIpAddressesAsync(userB.Id));

        var detection = await _security.DetectAbnormalLoginAsync(userB.Id, "203.0.113.7", "SomeNewAgent");
        Assert.False(detection.IsAbnormal);
        Assert.Equal(0, detection.RiskLevel);
    }

    [Fact]
    public async Task TenantAdmin_SecurityOverview_And_FrequentFailures_CoverOnlyOwnTenant()
    {
        var (adminA, userA, userB) = await SeedAsync();
        ActAs(adminA.Id, TenantA);

        // ★ 带 UserManager 的实例：总览里的「锁定用户数」（LockoutEnd > UtcNow）此前在 SQLite 上翻译不了、
        //   这里曾用没有 UserManager 的实例绕开；现在按租户裁剪的锁定计数也一并证到。
        var overview = await _security.GetSecurityOverviewAsync(hours: 24);
        Assert.True(overview.Succeeded, overview.Message);
        Assert.Equal(2, overview.Data!.TotalLoginAttempts);
        Assert.Equal(1, overview.Data!.DistinctUsers);
        Assert.Equal(0, overview.Data!.LockedOutUsers);

        var frequent = await _security.GetUsersWithFrequentFailuresAsync(hours: 24, minFailures: 1);
        Assert.True(frequent.Succeeded, frequent.Message);
        Assert.Contains(frequent.Data!, f => f.UserId == userA.Id);
        Assert.DoesNotContain(frequent.Data!, f => f.UserId == userB.Id);
    }

    [Fact]
    public async Task TenantAdmin_CanStillRead_OwnTenantUser()
    {
        var (adminA, userA, _) = await SeedAsync();
        ActAs(adminA.Id, TenantA);

        var logs = await _logs.GetUserLoginLogsAsync(userA.Id);
        Assert.True(logs.Succeeded, logs.Message);
        Assert.Equal(2, logs.Data!.Count());

        Assert.Equal(2, (await _security.GetRecentLoginsAsync(userA.Id)).Count());
        Assert.Contains("10.0.0.1", await _security.GetFrequentIpAddressesAsync(userA.Id));
    }

    [Fact]
    public async Task GlobalAdmin_WithNoTenantContext_SeesEverything_IncludingUnattributedRows()
    {
        var (_, _, userB) = await SeedAsync();
        ActAs(Guid.NewGuid(), null);

        var list = await _logs.GetPagedListAsync(new LoginLogQueryDto { PageIndex = 1, PageSize = 50 });
        Assert.True(list.Succeeded, list.Message);
        Assert.Equal(7, list.Data!.TotalCount);

        var logs = await _logs.GetUserLoginLogsAsync(userB.Id);
        Assert.True(logs.Succeeded, logs.Message);
        Assert.Equal(4, logs.Data!.Count());
    }

    /// <summary>登录链路上的异常登录检测没有已认证主体，租户上下文来自 header —— 不裁剪，检测照常跑。</summary>
    [Fact]
    public async Task SystemFlow_WithoutPrincipal_StillDetectsAbnormalLogins_AcrossTenants()
    {
        var (_, _, userB) = await SeedAsync();
        ActAsSystemInTenant(TenantA);

        var detection = await _security.DetectAbnormalLoginAsync(userB.Id, "203.0.113.7", "SomeNewAgent");

        Assert.True(detection.IsAbnormal);
    }
}
