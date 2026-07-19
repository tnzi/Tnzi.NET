using System.Linq.Expressions;
using Tnzi.Identity.Entities;

namespace Tnzi.Authorization.Tests.Services;

/// <summary>
/// Unit tests for <see cref="SuperAdminBootstrapper"/> — the startup
/// "first super admin" assignment. Key contract: it only acts while EVERY
/// existing super-admin role has zero members (recovery semantics), resolves
/// users by name case-insensitively, targets the first configured role that
/// exists, and never throws for missing users or missing Identity wiring.
/// </summary>
public class SuperAdminBootstrapperTests
{
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IUserRoleService> _userRoleService = new();

    private SuperAdminBootstrapper Build(Role[] roles, User[] users)
        => new(
            new Mock<ILogger<SuperAdminBootstrapper>>().Object,
            _userService.Object,
            _userRoleService.Object,
            RepoWith(roles),
            RepoWith(users));

    private static IRepository<TEntity, Guid> RepoWith<TEntity>(TEntity[] rows)
        where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        var mock = new Mock<IRepository<TEntity, Guid>>();
        mock.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<TEntity, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<TEntity, bool>> p, CancellationToken _) => rows.Where(p.Compile()).ToList());
        return mock.Object;
    }

    private static Role NewRole(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name, NormalizedName = name.ToUpperInvariant(),
    };

    private static User NewUser(string name) => new()
    {
        Id = Guid.NewGuid(), UserName = name, NormalizedUserName = name.ToUpperInvariant(),
    };

    private void SetRoleMembers(Guid roleId, params Guid[] members)
        => _userRoleService.Setup(s => s.GetRoleUserIdsAsync(roleId)).ReturnsAsync(members);

    [Fact]
    public async Task Assigns_listed_users_while_all_super_admin_roles_are_empty()
    {
        var role = NewRole("SuperAdmin");
        var user = NewUser("admin");
        SetRoleMembers(role.Id);
        _userService.Setup(s => s.AssignRolesAsync(user.Id, It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(Result.Success());

        var assigned = await Build([role], [user]).BootstrapAsync(["SuperAdmin"], ["admin"]);

        assigned.ShouldBe(1);
        _userService.Verify(s => s.AssignRolesAsync(
            user.Id, It.Is<IEnumerable<Guid>>(ids => ids.Single() == role.Id)), Times.Once);
    }

    [Fact]
    public async Task User_name_match_is_case_insensitive()
    {
        var role = NewRole("SuperAdmin");
        var user = NewUser("admin");
        SetRoleMembers(role.Id);
        _userService.Setup(s => s.AssignRolesAsync(user.Id, It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(Result.Success());

        var assigned = await Build([role], [user]).BootstrapAsync(["SuperAdmin"], ["ADMIN"]);

        assigned.ShouldBe(1);
    }

    [Fact]
    public async Task Skips_entirely_when_any_super_admin_role_has_members()
    {
        var seeded = NewRole("SuperAdmin");
        var legacy = NewRole("Root");
        SetRoleMembers(seeded.Id);
        SetRoleMembers(legacy.Id, Guid.NewGuid());

        var assigned = await Build([seeded, legacy], [NewUser("admin")])
            .BootstrapAsync(["SuperAdmin", "Root"], ["admin"]);

        assigned.ShouldBe(0);
        _userService.Verify(s => s.AssignRolesAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>()), Times.Never);
    }

    [Fact]
    public async Task Missing_users_are_skipped_without_failing_the_rest()
    {
        var role = NewRole("SuperAdmin");
        var known = NewUser("admin");
        SetRoleMembers(role.Id);
        _userService.Setup(s => s.AssignRolesAsync(known.Id, It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(Result.Success());

        var assigned = await Build([role], [known]).BootstrapAsync(["SuperAdmin"], ["ghost", "admin"]);

        assigned.ShouldBe(1);
        _userService.Verify(s => s.AssignRolesAsync(known.Id, It.IsAny<IEnumerable<Guid>>()), Times.Once);
    }

    [Fact]
    public async Task Target_role_follows_configured_order()
    {
        var root = NewRole("Root");
        var super = NewRole("SuperAdmin");
        var user = NewUser("admin");
        SetRoleMembers(root.Id);
        SetRoleMembers(super.Id);
        _userService.Setup(s => s.AssignRolesAsync(user.Id, It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(Result.Success());

        await Build([root, super], [user]).BootstrapAsync(["Root", "SuperAdmin"], ["admin"]);

        _userService.Verify(s => s.AssignRolesAsync(
            user.Id, It.Is<IEnumerable<Guid>>(ids => ids.Single() == root.Id)), Times.Once);
    }

    [Fact]
    public async Task Skips_when_no_configured_super_admin_role_exists()
    {
        var assigned = await Build([], [NewUser("admin")]).BootstrapAsync(["SuperAdmin"], ["admin"]);

        assigned.ShouldBe(0);
        _userService.Verify(s => s.AssignRolesAsync(It.IsAny<Guid>(), It.IsAny<IEnumerable<Guid>>()), Times.Never);
    }

    [Fact]
    public async Task Skips_when_identity_services_are_unavailable()
    {
        var bootstrapper = new SuperAdminBootstrapper(new Mock<ILogger<SuperAdminBootstrapper>>().Object);

        (await bootstrapper.BootstrapAsync(["SuperAdmin"], ["admin"])).ShouldBe(0);
    }

    [Fact]
    public async Task Failed_assignment_is_logged_not_thrown()
    {
        var role = NewRole("SuperAdmin");
        var user = NewUser("admin");
        SetRoleMembers(role.Id);
        _userService.Setup(s => s.AssignRolesAsync(user.Id, It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(Result.Failure("boom", 400));

        var assigned = await Build([role], [user]).BootstrapAsync(["SuperAdmin"], ["admin"]);

        assigned.ShouldBe(0);
    }
}
