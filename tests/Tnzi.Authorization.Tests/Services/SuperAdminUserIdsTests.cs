using System.Linq.Expressions;
using Tnzi.Identity.Entities;
using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Services;

/// <summary>
/// Unit tests for <see cref="FunctionAuthorizationService.GetSuperAdminUserIdsAsync"/> -
/// the forward "list every super-admin user id" lookup used to strip super admins out
/// of business-facing user listings (chat directory, group members, broadcast audience).
/// Repositories are mocked via the <c>ToListAsync(predicate)</c> overload (in-memory
/// filter), so no live EF provider is needed.
/// </summary>
public class SuperAdminUserIdsTests
{
    private static FunctionAuthorizationService Build(
        AuthOptions options,
        Mock<IUserRoleService>? userRoleService,
        Mock<IRepository<Role, Guid>>? roleRepo)
    {
        return new FunctionAuthorizationService(
            new Mock<IRepository<FunctionModule, Guid>>().Object,
            new Mock<IRepository<ModuleFunction, Guid>>().Object,
            new Mock<IRepository<RoleFunction, Guid>>().Object,
            new Mock<IRepository<UserFunction, Guid>>().Object,
            new Mock<IServiceProvider>().Object,
            userRoleService?.Object,
            functionAuthCache: null,
            options: MsOptions.Create(options),
            roleRepository: roleRepo?.Object);
    }

    private static Mock<IRepository<Role, Guid>> RoleRepoWith(params Role[] roles)
    {
        var mock = new Mock<IRepository<Role, Guid>>();
        mock.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<Role, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<Role, bool>> p, CancellationToken _) => roles.Where(p.Compile()).ToList());
        return mock;
    }

    [Fact]
    public async Task Returns_union_of_all_super_admin_role_members()
    {
        var role = new Role { Id = Guid.NewGuid(), Name = "SuperAdmin", NormalizedName = "SUPERADMIN" };
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var userRoleService = new Mock<IUserRoleService>();
        userRoleService.Setup(s => s.GetRoleUserIdsAsync(role.Id)).ReturnsAsync(new[] { u1, u2 });

        var service = Build(new AuthOptions { SuperAdminRoles = ["SuperAdmin"] }, userRoleService, RoleRepoWith(role));

        var ids = await service.GetSuperAdminUserIdsAsync();

        ids.ShouldBe(new[] { u1, u2 }, ignoreOrder: true);
    }

    [Fact]
    public async Task Role_name_match_is_case_insensitive()
    {
        // Configured with lower-case name; role row's NormalizedName is upper-cased.
        var role = new Role { Id = Guid.NewGuid(), Name = "SuperAdmin", NormalizedName = "SUPERADMIN" };
        var u1 = Guid.NewGuid();
        var userRoleService = new Mock<IUserRoleService>();
        userRoleService.Setup(s => s.GetRoleUserIdsAsync(role.Id)).ReturnsAsync(new[] { u1 });

        var service = Build(new AuthOptions { SuperAdminRoles = ["superadmin"] }, userRoleService, RoleRepoWith(role));

        (await service.GetSuperAdminUserIdsAsync()).ShouldContain(u1);
    }

    [Fact]
    public async Task Deduplicates_users_present_in_multiple_super_admin_roles()
    {
        var roleA = new Role { Id = Guid.NewGuid(), Name = "SuperAdmin", NormalizedName = "SUPERADMIN" };
        var roleB = new Role { Id = Guid.NewGuid(), Name = "Root", NormalizedName = "ROOT" };
        var shared = Guid.NewGuid();
        var onlyB = Guid.NewGuid();
        var userRoleService = new Mock<IUserRoleService>();
        userRoleService.Setup(s => s.GetRoleUserIdsAsync(roleA.Id)).ReturnsAsync(new[] { shared });
        userRoleService.Setup(s => s.GetRoleUserIdsAsync(roleB.Id)).ReturnsAsync(new[] { shared, onlyB });

        var service = Build(new AuthOptions { SuperAdminRoles = ["SuperAdmin", "Root"] }, userRoleService, RoleRepoWith(roleA, roleB));

        var ids = await service.GetSuperAdminUserIdsAsync();

        ids.Count.ShouldBe(2);
        ids.ShouldContain(shared);
        ids.ShouldContain(onlyB);
    }

    [Fact]
    public async Task Empty_when_no_super_admin_roles_configured()
    {
        var service = Build(new AuthOptions { SuperAdminRoles = [] }, new Mock<IUserRoleService>(), RoleRepoWith());

        (await service.GetSuperAdminUserIdsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Empty_when_role_repository_is_unavailable()
    {
        // Without a role repository the service can't resolve super-admin role ids →
        // it must fail open to "hide no one" (never over-hide business users).
        var service = Build(new AuthOptions { SuperAdminRoles = ["SuperAdmin"] }, new Mock<IUserRoleService>(), roleRepo: null);

        (await service.GetSuperAdminUserIdsAsync()).ShouldBeEmpty();
    }
}
