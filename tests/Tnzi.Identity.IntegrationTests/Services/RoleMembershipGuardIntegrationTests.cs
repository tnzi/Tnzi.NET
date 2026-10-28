using Microsoft.AspNetCore.Identity;
using Tnzi.Exceptions;
using Tnzi.Identity.Services;
using Tnzi.Security.Authorization;

namespace Tnzi.Identity.IntegrationTests.Services;

/// <summary>
/// 角色成员变更问的是成员版本的支配判定（<c>CanManageRoleMembershipAsync</c>），不是授权矩阵用的那一个。
/// </summary>
/// <remarks>
/// ★★★ 两者只差角色身上的休眠授权（指向已停用 / 已退役权限）：矩阵写入不算它们，否则一条休眠授权会把每个非超管锁在外面；
/// 成员变更必须算，否则「停用一个码 → 把自己挂进带着它的角色 → 重新启用」就能不经任何人授出而持有它。
/// 这里让两个判定给出相反的答案，断言成员变更听的是成员版本，且被拒时库里一行都没动。
/// </remarks>
public class RoleMembershipGuardIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IFunctionAuthorizationService> _functionAuthorization = new();
    private readonly UserService _users;

    public RoleMembershipGuardIntegrationTests()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _functionAuthorization.Setup(a => a.IsSuperAdminAsync(It.IsAny<Guid>())).ReturnsAsync(false);
        // 矩阵版本放行、成员版本拒绝：角色只带着一条休眠授权时正是这个形状。
        _functionAuthorization.Setup(a => a.CanManageRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ReturnsAsync(true);

        _users = new UserService(
            UserManager,
            ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            CreateRepository<User>(),
            ServiceProvider,
            eventBus: EventBusMock.Object,
            currentUser: _currentUser.Object,
            functionAuthorization: _functionAuthorization.Object);
    }

    private async Task<(User Target, Role Role)> SeedAsync(bool membershipAllowed)
    {
        var target = await CreateUserAsync(email: "target@example.com");
        var role = new Role { Id = Guid.NewGuid(), Name = "PayrollManager", NormalizedName = "PAYROLLMANAGER" };
        DbContext.Roles.Add(role);
        await SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        _functionAuthorization.Setup(a => a.CanManageRoleMembershipAsync(It.IsAny<Guid>(), role.Id)).ReturnsAsync(membershipAllowed);
        return (target, role);
    }

    private async Task<bool> IsInRoleAsync(Guid userId, Guid roleId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
    }

    [Fact]
    public async Task Assigning_a_role_the_caller_cannot_manage_membership_of_is_forbidden_and_writes_nothing()
    {
        var (target, role) = await SeedAsync(membershipAllowed: false);

        var result = await _users.AssignRolesAsync(target.Id, [role.Id]);

        Assert.False(result.Succeeded);
        Assert.Equal(403, result.Code);
        Assert.Equal(ErrorCodes.FORBIDDEN, result.ErrorCode);
        Assert.False(await IsInRoleAsync(target.Id, role.Id));
    }

    [Fact]
    public async Task Removing_a_role_the_caller_cannot_manage_membership_of_is_forbidden_and_keeps_the_member()
    {
        var (target, role) = await SeedAsync(membershipAllowed: false);
        DbContext.UserRoles.Add(new UserRole { UserId = target.Id, RoleId = role.Id });
        await SaveChangesAsync();

        var result = await _users.RemoveRolesAsync(target.Id, [role.Id]);

        Assert.Equal(403, result.Code);
        Assert.True(await IsInRoleAsync(target.Id, role.Id));
    }

    [Fact]
    public async Task Membership_changes_go_through_when_the_membership_check_allows_them()
    {
        var (target, role) = await SeedAsync(membershipAllowed: true);

        Assert.True((await _users.AssignRolesAsync(target.Id, [role.Id])).Succeeded);
        Assert.True(await IsInRoleAsync(target.Id, role.Id));
    }
}
