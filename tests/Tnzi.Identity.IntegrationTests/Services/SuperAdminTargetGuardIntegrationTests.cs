using Microsoft.AspNetCore.Identity;
using Tnzi.Identity.Services;
using Tnzi.Identity.Organization.Services;
using Tnzi.MultiTenancy;
using Tnzi.Results;
using Tnzi.Exceptions;
using Tnzi.Security.Authorization;

namespace Tnzi.Identity.IntegrationTests.Services;

// 见 IntegrationTestBase.cs 顶部：`Organization` 的 using 必须在命名空间体内。
using Tnzi.Identity.Organization.Entities;

/// <summary>
/// 「非超管不能替超管做主」铺到 <c>admin/users</c> 的每一个针对已有账号的写操作上。
/// </summary>
/// <remarks>
/// 只守重置密码 / 2FA / 允许列表不够：持 <c>user.update</c> 的普通管理员把超管的邮箱改成自己的、
/// 再替这个地址担保（确认位），就能走找回密码拿下超管账号；停用 / 锁定 / 删除则直接把超管锁在门外。
/// 每条用例都断言两件事：回答是 403 <c>FORBIDDEN</c>，且库里的数据<b>一个字节都没动</b>。
/// 走真实 <c>UserManager</c> + SQLite，任何「先写后拒」的顺序错误都会在重读时暴露。
/// </remarks>
public class SuperAdminTargetGuardIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IFunctionAuthorizationService> _functionAuthorization = new();
    private readonly Mock<ISessionRevocationService> _sessionRevocation = new();
    private readonly UserService _users;
    private readonly OrganizationService _organizations;

    private readonly HashSet<Guid> _superAdmins = [];

    public SuperAdminTargetGuardIntegrationTests()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _functionAuthorization.Setup(a => a.IsSuperAdminAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => _superAdmins.Contains(id));
        // 角色支配护栏另有其责：这里让它恒放行，确保拦下来的只能是超管目标护栏。
        _functionAuthorization.Setup(a => a.CanManageRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(true);

        _users = new UserService(
            UserManager,
            ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            CreateRepository<User>(),
            ServiceProvider,
            eventBus: EventBusMock.Object,
            currentUser: _currentUser.Object,
            cache: Cache,
            functionAuthorization: _functionAuthorization.Object,
            sessionRevocation: _sessionRevocation.Object);
        _organizations = new OrganizationService(
            CreateRepository<Organization>(),
            ServiceProvider,
            DbContext,
            eventBus: EventBusMock.Object,
            currentUser: _currentUser.Object,
            multiTenancyOptions: Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions()),
            cache: Cache,
            userManager: UserManager,
            functionAuthorization: _functionAuthorization.Object);
    }

    private sealed record Seed(User Admin, User Root, User Plain, Role Role, Organization Org);

    private async Task<Seed> SeedAsync(bool actorIsSuperAdmin)
    {
        var admin = await CreateUserAsync(email: "admin@example.com");
        var root = await CreateUserAsync(email: "root@example.com", emailConfirmed: false, phoneNumber: "15550000001", phoneConfirmed: false);
        var plain = await CreateUserAsync(email: "plain@example.com");

        var role = new Role { Id = Guid.NewGuid(), Name = "Staff", NormalizedName = "STAFF" };
        var org = new Organization { Id = Guid.NewGuid(), Name = "HQ", Level = 1, SortOrder = 1, IsEnabled = true };
        org.Path = $"/{org.Id}/";
        DbContext.Roles.Add(role);
        DbContext.Organizations.Add(org);
        await SaveChangesAsync();

        _superAdmins.Add(root.Id);
        if (actorIsSuperAdmin)
        {
            _superAdmins.Add(admin.Id);
        }

        _currentUser.Setup(u => u.Id).Returns(admin.Id);
        DbContext.ChangeTracker.Clear();
        return new Seed(admin, root, plain, role, org);
    }

    private async Task<User> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == id);
    }

    private async Task<bool> IsInRoleAsync(Guid userId, Guid roleId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
    }

    private async Task PutInRoleAsync(Guid userId, Guid roleId)
    {
        DbContext.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        await SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private static void AssertForbidden(Result result)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.FORBIDDEN, result.ErrorCode);
    }

    // ── 改资料 / 改联系方式 / 替地址担保 ───────────────────────────────────

    [Fact]
    public async Task Update_ChangingASuperAdminsEmail_ByANonSuperAdmin_IsForbidden_AndChangesNothing()
    {
        var seed = await SeedAsync(actorIsSuperAdmin: false);

        var result = await _users.UpdateAsync(seed.Root.Id, new UpdateUserDto { Email = "attacker@example.com", PhoneNumber = "15559999999" });

        AssertForbidden(result);
        var root = await ReloadAsync(seed.Root.Id);
        Assert.Equal("root@example.com", root.Email);
        Assert.Equal("15550000001", root.PhoneNumber);
    }

    [Fact]
    public async Task ConfirmContact_OnASuperAdmin_ByANonSuperAdmin_IsForbidden_AndLeavesTheFlagsAlone()
    {
        var seed = await SeedAsync(actorIsSuperAdmin: false);

        var result = await _users.ConfirmContactAsync(seed.Root.Id, confirmEmail: true, confirmPhoneNumber: true);

        AssertForbidden(result);
        var root = await ReloadAsync(seed.Root.Id);
        Assert.False(root.EmailConfirmed);
        Assert.False(root.PhoneNumberConfirmed);
    }

    // ── 启用 / 停用 / 锁定 / 解锁 ─────────────────────────────────────────

    [Fact]
    public async Task DisableAndLock_OnASuperAdmin_ByANonSuperAdmin_AreForbidden_AndLeaveTheAccountOpen()
    {
        var seed = await SeedAsync(actorIsSuperAdmin: false);

        AssertForbidden(await _users.DisableAsync(seed.Root.Id, "x"));
        AssertForbidden(await _users.LockAsync(seed.Root.Id));

        Assert.Null((await ReloadAsync(seed.Root.Id)).LockoutEnd);
        _sessionRevocation.Verify(
            s => s.RevokeUserSessionsAsync(It.IsAny<Guid>(), It.IsAny<SessionRevocationReason>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    [Fact]
    public async Task EnableAndUnlock_OnASuperAdmin_ByANonSuperAdmin_AreForbidden_AndLeaveTheLockInPlace()
    {
        var seed = await SeedAsync(actorIsSuperAdmin: false);
        var lockedUntil = DateTimeOffset.UtcNow.AddYears(1);
        var root = await UserManager.FindByIdAsync(seed.Root.Id.ToString());
        await UserManager.SetLockoutEndDateAsync(root!, lockedUntil);
        DbContext.ChangeTracker.Clear();

        AssertForbidden(await _users.EnableAsync(seed.Root.Id));
        AssertForbidden(await _users.UnlockAsync(seed.Root.Id));

        Assert.NotNull((await ReloadAsync(seed.Root.Id)).LockoutEnd);
    }

    // ── 删除 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ASuperAdmin_ByANonSuperAdmin_IsForbidden_AndDeletesNothing()
    {
        var seed = await SeedAsync(actorIsSuperAdmin: false);

        AssertForbidden(await _users.DeleteAsync(seed.Root.Id));

        Assert.False((await ReloadAsync(seed.Root.Id)).IsDeleted);
    }

    /// <summary>
    /// 批量删除不是事务：只要批里有一个超管，整批拒绝，普通账号也一个都不删 —— 不留半批结果。
    /// </summary>
    [Fact]
    public async Task DeleteMany_ContainingASuperAdmin_ByANonSuperAdmin_RejectsTheWholeBatch()
    {
        var seed = await SeedAsync(actorIsSuperAdmin: false);

        AssertForbidden(await _users.DeleteManyAsync([seed.Plain.Id, seed.Root.Id]));

        Assert.False((await ReloadAsync(seed.Plain.Id)).IsDeleted);
        Assert.False((await ReloadAsync(seed.Root.Id)).IsDeleted);
    }

    /// <summary>对照组：批里没有超管时，非超管照常删除。</summary>
    [Fact]
    public async Task DeleteMany_WithoutASuperAdmin_ByANonSuperAdmin_StillWorks()
    {
        var seed = await SeedAsync(actorIsSuperAdmin: false);

        var result = await _users.DeleteManyAsync([seed.Plain.Id]);

        Assert.True(result.Succeeded, result.Message);
        Assert.True((await ReloadAsync(seed.Plain.Id)).IsDeleted);
    }

    [Fact]
    public async Task UpdateMany_ContainingASuperAdmin_ByANonSuperAdmin_RejectsTheWholeBatch()
    {
        var seed = await SeedAsync(actorIsSuperAdmin: false);

        var result = await _users.UpdateManyAsync(
        [
            (seed.Plain.Id, new UpdateUserDto { PhoneNumber = "15558888888" }),
            (seed.Root.Id, new UpdateUserDto { Email = "attacker@example.com" }),
        ]);

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.FORBIDDEN, result.ErrorCode);
        Assert.NotEqual("15558888888", (await ReloadAsync(seed.Plain.Id)).PhoneNumber);
        Assert.Equal("root@example.com", (await ReloadAsync(seed.Root.Id)).Email);
    }

    // ── 角色成员 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 角色支配护栏（<c>CanManageRoleAsync</c>）只问「这个角色调用者管不管得了」，不问目标是谁；
    /// 一个调用者管得了的角色照样能从超管身上摘下或挂上。目标维度由超管目标护栏补齐。
    /// </summary>
    [Fact]
    public async Task AssignAndRemoveRoles_OnASuperAdmin_ByANonSuperAdmin_AreForbidden_EvenForAManageableRole()
    {
        var seed = await SeedAsync(actorIsSuperAdmin: false);
        var other = new Role { Id = Guid.NewGuid(), Name = "Auditor", NormalizedName = "AUDITOR" };
        DbContext.Roles.Add(other);
        await SaveChangesAsync();
        await PutInRoleAsync(seed.Root.Id, seed.Role.Id);

        AssertForbidden(await _users.AssignRolesAsync(seed.Root.Id, [other.Id]));
        AssertForbidden(await _users.RemoveRolesAsync(seed.Root.Id, [seed.Role.Id]));

        Assert.False(await IsInRoleAsync(seed.Root.Id, other.Id));
        Assert.True(await IsInRoleAsync(seed.Root.Id, seed.Role.Id));
    }

    // ── 组织归属（组织子模块，同一张 User 表） ─────────────────────────────

    [Fact]
    public async Task AssignAndRemoveOrganization_OnASuperAdmin_ByANonSuperAdmin_AreForbidden()
    {
        var seed = await SeedAsync(actorIsSuperAdmin: false);

        AssertForbidden(await _organizations.AssignUserToOrganizationAsync(seed.Root.Id, seed.Org.Id));
        Assert.Null((await ReloadAsync(seed.Root.Id)).OrganizationId);

        var root = await UserManager.FindByIdAsync(seed.Root.Id.ToString());
        root!.OrganizationId = seed.Org.Id;
        await UserManager.UpdateAsync(root);
        DbContext.ChangeTracker.Clear();

        AssertForbidden(await _organizations.RemoveUserFromOrganizationAsync(seed.Root.Id));
        Assert.Equal(seed.Org.Id, (await ReloadAsync(seed.Root.Id)).OrganizationId);
    }

    // ── 超管调用者：一律放行 ────────────────────────────────────────────

    [Fact]
    public async Task ASuperAdmin_CanStillDoEveryOneOfTheseToAnotherSuperAdmin()
    {
        var seed = await SeedAsync(actorIsSuperAdmin: true);

        var update = await _users.UpdateAsync(seed.Root.Id, new UpdateUserDto { Email = "root2@example.com" });
        Assert.True(update.Succeeded, update.Message);
        Assert.True((await _users.ConfirmContactAsync(seed.Root.Id, confirmEmail: true, confirmPhoneNumber: null)).Succeeded);
        Assert.True((await _users.LockAsync(seed.Root.Id)).Succeeded);
        Assert.True((await _users.UnlockAsync(seed.Root.Id)).Succeeded);
        Assert.True((await _users.DisableAsync(seed.Root.Id, "x")).Succeeded);
        Assert.True((await _users.EnableAsync(seed.Root.Id)).Succeeded);
        Assert.True((await _users.AssignRolesAsync(seed.Root.Id, [seed.Role.Id])).Succeeded);
        Assert.True((await _users.RemoveRolesAsync(seed.Root.Id, [seed.Role.Id])).Succeeded);
        Assert.True((await _organizations.AssignUserToOrganizationAsync(seed.Root.Id, seed.Org.Id)).Succeeded);
        Assert.True((await _organizations.RemoveUserFromOrganizationAsync(seed.Root.Id)).Succeeded);
        Assert.True((await _users.UpdateManyAsync([(seed.Root.Id, new UpdateUserDto { PhoneNumber = "15557777777" })])).Succeeded);

        var root = await ReloadAsync(seed.Root.Id);
        Assert.Equal("root2@example.com", root.Email);
        Assert.True(root.EmailConfirmed);
        Assert.Equal("15557777777", root.PhoneNumber);

        var deleted = await _users.DeleteManyAsync([seed.Plain.Id, seed.Root.Id]);
        Assert.True(deleted.Succeeded, deleted.Message);
        Assert.True((await ReloadAsync(seed.Root.Id)).IsDeleted);
        Assert.True((await ReloadAsync(seed.Plain.Id)).IsDeleted);
    }

    [Fact]
    public async Task ASuperAdmin_CanDeleteASingleSuperAdmin()
    {
        var seed = await SeedAsync(actorIsSuperAdmin: true);

        var result = await _users.DeleteAsync(seed.Root.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.True((await ReloadAsync(seed.Root.Id)).IsDeleted);
    }
}
