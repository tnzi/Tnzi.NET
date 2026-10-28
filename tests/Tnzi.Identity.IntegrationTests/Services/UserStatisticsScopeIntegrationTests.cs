using Microsoft.AspNetCore.Identity;
using Tnzi.EFCore;
using Tnzi.Identity.Services;
using Tnzi.MultiTenancy;

namespace Tnzi.Identity.IntegrationTests.Services;

// 见 IntegrationTestBase.cs 顶部：`Organization` 的 using 必须在命名空间体内。
using Tnzi.Identity.Organization.Entities;

/// <summary>
/// 用户统计（<c>GET admin/users/statistics</c>）的每一个数字都出自同一个集合：
/// 当前租户范围内、未删除、（给了组织时）该组织下的账号。
/// </summary>
/// <remarks>
/// 此前只有总数 / 活跃 / 锁定 / 近七日注册四项走租户范围，另两项各自重查：
/// 「有组织的人数」数的是全平台所有租户，「角色人数」直接数关联表，
/// 既不看租户也不排除软删账号（关联行在软删之后仍在），于是与 <c>GET admin/roles/{id}/user-count</c> 对不上。
/// </remarks>
public class UserStatisticsScopeIntegrationTests : RelationalIdentityIntegrationTestBase
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly Mock<ICurrentTenant> _currentTenant = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly UserService _users;

    private Guid? _tenantId;

    public UserStatisticsScopeIntegrationTests()
        : base(configureServices: services => services.Configure<MultiTenancyOptions>(o => o.Enabled = true))
    {
        _currentTenant.Setup(t => t.Id).Returns(() => _tenantId);
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(() => _tenantId);

        _users = new UserService(
            UserManager,
            ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            CreateRepository<User>(),
            ServiceProvider,
            eventBus: EventBusMock.Object,
            currentUser: _currentUser.Object,
            userRoleRepository: new EFCoreRepository<TestIdentityDbContext, UserRole>(DbContext, serviceProvider: ServiceProvider),
            currentTenant: _currentTenant.Object,
            multiTenancyOptions: Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true }));
    }

    private sealed record Seeded(User AdminA, Guid OrgA, Guid OrgB, Guid RoleId);

    /// <summary>
    /// 租户 A：管理员（在组织 A、在角色里）、普通成员（在角色里）、一个已软删的成员（在组织 A、在角色里）。
    /// 租户 B：一个成员（在组织 B、在角色里）。全局账号：一个（在组织 A）。
    /// </summary>
    private async Task<Seeded> SeedAsync()
    {
        var orgA = new Organization { Id = Guid.NewGuid(), Name = "A", Path = $"/{Guid.NewGuid()}/", Level = 1, IsEnabled = true, TenantId = TenantA };
        var orgB = new Organization { Id = Guid.NewGuid(), Name = "B", Path = $"/{Guid.NewGuid()}/", Level = 1, IsEnabled = true, TenantId = TenantB };
        var role = new Role { Id = Guid.NewGuid(), Name = "Auditors", NormalizedName = "AUDITORS" };
        DbContext.Organizations.AddRange(orgA, orgB);
        DbContext.Roles.Add(role);
        await SaveChangesAsync();

        var adminA = await CreateUserAsync(email: "admin-a@example.com");
        var memberA = await CreateUserAsync(email: "member-a@example.com");
        var deletedA = await CreateUserAsync(email: "deleted-a@example.com");
        var memberB = await CreateUserAsync(email: "member-b@example.com");
        var global = await CreateUserAsync(email: "global@example.com");

        adminA.TenantId = TenantA;
        adminA.OrganizationId = orgA.Id;
        memberA.TenantId = TenantA;
        deletedA.TenantId = TenantA;
        deletedA.OrganizationId = orgA.Id;
        deletedA.IsDeleted = true;
        memberB.TenantId = TenantB;
        memberB.OrganizationId = orgB.Id;
        global.OrganizationId = orgA.Id;

        foreach (var member in new[] { adminA, memberA, deletedA, memberB })
        {
            DbContext.UserRoles.Add(new UserRole { UserId = member.Id, RoleId = role.Id });
        }

        await SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return new Seeded(adminA, orgA.Id, orgB.Id, role.Id);
    }

    private void ActAs(Guid adminId, Guid? tenantId)
    {
        _currentUser.Setup(u => u.Id).Returns(adminId);
        _tenantId = tenantId;
    }

    [Fact]
    public async Task TenantAdmin_UsersByOrganization_CountsOnlyOwnTenantUsers()
    {
        var seeded = await SeedAsync();
        ActAs(seeded.AdminA.Id, TenantA);

        var stats = await _users.GetStatisticsAsync();

        Assert.True(stats.Succeeded, stats.Message);
        Assert.Equal(2, stats.Data!.TotalUsers);
        // 只有租户 A 的管理员在组织里：B 的成员、全局账号与已删账号都不算
        Assert.Equal(1, stats.Data.UsersByOrganization);
    }

    [Fact]
    public async Task TenantAdmin_UsersByRole_CountsOnlyOwnTenantLiveMembers()
    {
        var seeded = await SeedAsync();
        ActAs(seeded.AdminA.Id, TenantA);

        var stats = await _users.GetStatisticsAsync(roleId: seeded.RoleId);

        Assert.True(stats.Succeeded, stats.Message);
        // 管理员与普通成员；已软删的那位与租户 B 的成员都不算
        Assert.Equal(2, stats.Data!.UsersByRole);
    }

    [Fact]
    public async Task UsersByRole_WithAnOrganization_CountsTheRoleMembersInThatOrganization()
    {
        var seeded = await SeedAsync();
        ActAs(seeded.AdminA.Id, TenantA);

        var stats = await _users.GetStatisticsAsync(organizationId: seeded.OrgA, roleId: seeded.RoleId);

        Assert.True(stats.Succeeded, stats.Message);
        Assert.Equal(1, stats.Data!.TotalUsers);
        Assert.Equal(1, stats.Data.UsersByOrganization);
        Assert.Equal(1, stats.Data.UsersByRole);
    }

    [Fact]
    public async Task GlobalAdmin_CountsEveryTenant_ButNeverDeletedAccounts()
    {
        var seeded = await SeedAsync();
        ActAs(Guid.NewGuid(), null);

        var stats = await _users.GetStatisticsAsync(roleId: seeded.RoleId);

        Assert.True(stats.Succeeded, stats.Message);
        Assert.Equal(4, stats.Data!.TotalUsers);
        // 管理员 A、B 的成员、全局账号
        Assert.Equal(3, stats.Data.UsersByOrganization);
        // 管理员 A、成员 A、成员 B；已删的那位不算
        Assert.Equal(3, stats.Data.UsersByRole);
    }
}
