using Microsoft.AspNetCore.Identity;
using Tnzi.Identity.Services;
using Microsoft.Extensions.Options;
using Tnzi.MultiTenancy;
using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 多租户开启时，用户管理端点按当前租户裁剪。
///
/// ★★★ <c>User</c> 刻意不实现 <c>IMultiTenant</c>（登录链路 + <c>TenantId = null</c> 的全局账号），
/// 所以没有全局过滤器替它把关；而此前也没有任何补偿性校验 —— 租户 A 的管理员凭框架自带的
/// <c>user.*</c> 权限码能列出全部租户的用户、重置别家租户用户的密码（顺手踢掉对方全部会话）、
/// 删除别家租户的账号，全部 200。<c>Role</c> / <c>UserDetail</c> 又都被过滤，形成不对称，
/// 而文档把 <c>Enabled=true</c> 写成「完整多租户隔离模式」。
///
/// 口径：当前租户非空 ⇒ 只有同租户账号在范围内，本人恒在范围内；越界一律 404（不泄露存在性）。
/// 当前租户为空（全局管理员）⇒ 不裁剪。纯 LINQ 谓词，SQLite 不会掩盖。
/// </summary>
public class UserTenantScopeIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly Mock<ICurrentTenant> _currentTenant = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ISessionRevocationService> _sessionRevocation = new();
    private readonly UserService _users;
    private readonly PasswordService _passwords;

    private Guid? _tenantId;

    public UserTenantScopeIntegrationTests()
        : base(configureServices: services => services.Configure<MultiTenancyOptions>(o => o.Enabled = true))
    {
        _currentTenant.Setup(t => t.Id).Returns(() => _tenantId);
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(() => _tenantId);

        var multiTenancy = Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true });
        _users = new UserService(
            UserManager,
            ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            CreateRepository<User>(),
            ServiceProvider,
            eventBus: EventBusMock.Object,
            currentUser: _currentUser.Object,
            cache: Cache,
            currentTenant: _currentTenant.Object,
            multiTenancyOptions: multiTenancy,
            sessionRevocation: _sessionRevocation.Object);
        _passwords = new PasswordService(
            UserManager,
            ServiceProvider.GetRequiredService<IOptionsSnapshot<IdentityOptions>>(),
            ServiceProvider,
            eventBus: EventBusMock.Object,
            currentUser: _currentUser.Object,
            sessionRevocation: _sessionRevocation.Object,
            currentTenant: _currentTenant.Object,
            multiTenancyOptions: multiTenancy);
    }

    private void ActAs(Guid adminId, Guid? tenantId)
    {
        _currentUser.Setup(u => u.Id).Returns(adminId);
        _tenantId = tenantId;
    }

    private async Task<User> CreateTenantUserAsync(Guid? tenantId, string email)
    {
        var user = await CreateUserAsync(email: email);
        user.TenantId = tenantId;
        await SaveChangesAsync();
        return user;
    }

    private async Task<(User AdminA, User UserA, User UserB, User Global)> SeedAsync()
    {
        var adminA = await CreateTenantUserAsync(TenantA, "admin-a@example.com");
        var userA = await CreateTenantUserAsync(TenantA, "user-a@example.com");
        var userB = await CreateTenantUserAsync(TenantB, "user-b@example.com");
        var global = await CreateTenantUserAsync(null, "global@example.com");
        DbContext.ChangeTracker.Clear();
        return (adminA, userA, userB, global);
    }

    private async Task<User> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        // 软删过的行也要读得到（批量删除的断言靠它）。
        return await DbContext.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == id);
    }

    [Fact]
    public async Task TenantAdmin_ListsOnlyOwnTenantUsers()
    {
        var (adminA, userA, userB, global) = await SeedAsync();
        ActAs(adminA.Id, TenantA);

        var result = await _users.GetListAsync(new UserListQueryDto { PageIndex = 1, PageSize = 50 });

        Assert.True(result.Succeeded, result.Message);
        var ids = result.Data!.Items.Select(u => u.Id).ToHashSet();
        Assert.Contains(adminA.Id, ids);
        Assert.Contains(userA.Id, ids);
        Assert.DoesNotContain(userB.Id, ids);
        Assert.DoesNotContain(global.Id, ids);
    }

    /// <summary>
    /// 统计（<c>GetStatisticsAsync</c>）、导出与列表总数走同一个 <c>Scope.Apply</c>。
    /// ★ 统计那一半此前在这里被绕开（<c>LockoutEnd &lt;= UtcNow</c> 在 SQLite 上翻译不了），
    /// 于是租户裁剪从没在统计路径上被证过；<c>LockoutEndSqliteQueryTests</c> 修好翻译之后补上。
    /// </summary>
    [Fact]
    public async Task TenantAdmin_Statistics_Export_And_ListTotal_CoverOnlyOwnTenant()
    {
        var (adminA, _, userB, _) = await SeedAsync();
        ActAs(adminA.Id, TenantA);

        var list = await _users.GetListAsync(new UserListQueryDto { PageIndex = 1, PageSize = 50 });
        Assert.True(list.Succeeded, list.Message);
        Assert.Equal(2, list.Data!.TotalCount);

        var stats = await _users.GetStatisticsAsync();
        Assert.True(stats.Succeeded, stats.Message);
        Assert.Equal(2, stats.Data!.TotalUsers);
        Assert.Equal(2, stats.Data.ActiveUsers);
        Assert.Equal(0, stats.Data.LockedUsers);

        var csv = await _users.ExportUsersCsvAsync();
        Assert.True(csv.Succeeded, csv.Message);
        Assert.DoesNotContain(userB.Email!, csv.Data);
    }

    [Fact]
    public async Task TenantAdmin_CannotRead_OtherTenantUser_EvenThroughTheCache()
    {
        var (adminA, _, userB, _) = await SeedAsync();

        // 先以全局管理员身份把 B 用户读进缓存（缓存键不含租户）。
        ActAs(Guid.NewGuid(), null);
        var warmed = await _users.GetByIdAsync(userB.Id);
        Assert.True(warmed.Succeeded, warmed.Message);

        ActAs(adminA.Id, TenantA);
        var result = await _users.GetByIdAsync(userB.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task TenantAdmin_WriteActions_OnOtherTenantUser_Return404_AndChangeNothing()
    {
        var (adminA, _, userB, global) = await SeedAsync();
        ActAs(adminA.Id, TenantA);

        Assert.Equal(404, (await _users.DisableAsync(userB.Id, "x")).Code);
        Assert.Equal(404, (await _users.LockAsync(userB.Id)).Code);
        Assert.Equal(404, (await _users.UpdateAsync(userB.Id, new UpdateUserDto { PhoneNumber = "19990000000" })).Code);
        Assert.Equal(404, (await _users.DeleteAsync(userB.Id)).Code);
        Assert.Equal(404, (await _users.DisableAsync(global.Id, "x")).Code);

        var b = await ReloadAsync(userB.Id);
        Assert.Null(b.LockoutEnd);
        Assert.False(b.IsDeleted);
        Assert.NotEqual("19990000000", b.PhoneNumber);
        _sessionRevocation.Verify(
            s => s.RevokeUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    [Fact]
    public async Task TenantAdmin_BatchDelete_SkipsOtherTenantUsers()
    {
        var (adminA, userA, userB, _) = await SeedAsync();
        ActAs(adminA.Id, TenantA);

        var result = await _users.DeleteManyAsync([userA.Id, userB.Id]);

        Assert.True(result.Succeeded, result.Message);
        Assert.True((await ReloadAsync(userA.Id)).IsDeleted);
        Assert.False((await ReloadAsync(userB.Id)).IsDeleted);
    }

    [Fact]
    public async Task TenantAdmin_ResetPassword_OnOtherTenantUser_Returns404_AndDoesNotRevokeSessions()
    {
        var (adminA, _, userB, _) = await SeedAsync();
        var hashBefore = (await ReloadAsync(userB.Id)).PasswordHash;
        ActAs(adminA.Id, TenantA);

        var result = await _passwords.ResetPasswordByAdminAsync(userB.Id, "NewPassword123!");

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        Assert.Equal(hashBefore, (await ReloadAsync(userB.Id)).PasswordHash);
        _sessionRevocation.Verify(
            s => s.RevokeUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    [Fact]
    public async Task TenantAdmin_CanStillManage_OwnTenantUser()
    {
        var (adminA, userA, _, _) = await SeedAsync();
        ActAs(adminA.Id, TenantA);

        var disabled = await _users.DisableAsync(userA.Id, "x");
        Assert.True(disabled.Succeeded, disabled.Message);
        Assert.NotNull((await ReloadAsync(userA.Id)).LockoutEnd);

        var reset = await _passwords.ResetPasswordByAdminAsync(userA.Id, "NewPassword123!");
        Assert.True(reset.Succeeded, reset.Message);
    }

    [Fact]
    public async Task GlobalAdmin_WithNoTenantContext_SeesAllUsers()
    {
        var (adminA, userA, userB, global) = await SeedAsync();
        ActAs(global.Id, null);

        var result = await _users.GetListAsync(new UserListQueryDto { PageIndex = 1, PageSize = 50 });

        Assert.True(result.Succeeded, result.Message);
        var ids = result.Data!.Items.Select(u => u.Id).ToHashSet();
        Assert.Superset(new HashSet<Guid> { adminA.Id, userA.Id, userB.Id, global.Id }, ids);
        Assert.Equal(200, (await _users.GetByIdAsync(userB.Id)).Code ?? 200);
        Assert.True((await _users.GetByIdAsync(userB.Id)).Succeeded);
    }

    /// <summary>
    /// <c>DefaultTenantId</c> 会把一个全局账号放进某个租户上下文里；自助端点走同一批服务方法，
    /// 他必须仍然读得到自己的资料 —— 但别家租户的账号照样看不见。
    /// </summary>
    [Fact]
    public async Task Self_IsAlwaysInScope_EvenWhenAGlobalAccountSitsInATenantContext()
    {
        var (_, _, userB, global) = await SeedAsync();
        ActAs(global.Id, TenantA);

        var self = await _users.GetByIdAsync(global.Id);
        Assert.True(self.Succeeded, self.Message);

        var profile = await _users.UpdateProfileAsync(global.Id, new UpdateProfileDto { Nickname = "me" });
        Assert.True(profile.Succeeded, profile.Message);

        Assert.Equal(404, (await _users.GetByIdAsync(userB.Id)).Code);
    }
}
