using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using IdentityRole = Tnzi.Identity.Entities.Role;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 成员变更的支配判定（<see cref="FunctionAuthorizationService.CanManageRoleMembershipAsync"/>）把角色的休眠授权一并计入；
/// 授权矩阵的写入（<see cref="FunctionAuthorizationService.CanManageRoleAsync"/>）仍只算生效码。真实 SQLite。
/// </summary>
/// <remarks>
/// ★★★ 守的是一条自给自足的提权：持有 a 与功能目录写码、用户角色写码的弱管理员，先停用码 X，
/// 再把自己挂进授有 {a, X} 的角色（只看生效码，{a} ⊆ 自己，放行），最后重新启用 X，
/// 全程无人授出就持有了 X。退役同理：声明 X 的模块某次部署没加载，X 退役；下次加载，授权原样恢复。
/// </remarks>
public class RoleMembershipDominanceIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private static readonly Guid GrantorId = TestHelper.DefaultTestUserId;
    private static readonly Guid GrantorRoleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid PayrollRoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Mock<IUserRoleService> _userRoleService = new();
    private readonly AuthOptions _authOptions = new() { SuperAdminRoles = ["SuperAdmin"] };
    private string[] _grantorRoleNames = ["Staff"];

    public RoleMembershipDominanceIntegrationTests()
    {
        _userRoleService
            .Setup(s => s.GetUserRolesAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(() => new Dictionary<Guid, IEnumerable<string>> { [GrantorId] = _grantorRoleNames });
        _userRoleService
            .Setup(s => s.GetUserRoleIdsAsync(GrantorId))
            .ReturnsAsync([GrantorRoleId]);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<FunctionModule>(services);
        AddRepo<ModuleFunction>(services);
        AddRepo<RoleFunction>(services);
        AddRepo<UserFunction>(services);
        AddRepo<IdentityRole>(services);

        services.AddScoped(_ => _userRoleService.Object);
        services.AddScoped(_ => MsOptions.Create(_authOptions));

        services.AddScoped(sp => new FunctionAuthorizationService(
            sp.GetRequiredService<IRepository<FunctionModule, Guid>>(),
            sp.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
            sp.GetRequiredService<IRepository<RoleFunction, Guid>>(),
            sp.GetRequiredService<IRepository<UserFunction, Guid>>(),
            sp,
            sp.GetRequiredService<IUserRoleService>(),
            functionAuthCache: null,
            options: sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>(),
            roleRepository: sp.GetRequiredService<IRepository<IdentityRole, Guid>>()));
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<AuthorizationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<AuthorizationTestDbContext>(), serviceProvider: sp));
    }

    private FunctionAuthorizationService Service()
        => ServiceProvider.CreateScope().ServiceProvider.GetRequiredService<FunctionAuthorizationService>();

    /// <summary>授权者经自己的角色持有 a；PayrollManager 授有 {a, X}。返回 (a, X)。</summary>
    private async Task<(ModuleFunction A, ModuleFunction X)> SeedAsync(bool grantorHoldsX = false)
    {
        var module = new FunctionModule { Id = Guid.NewGuid(), Name = "Finance", Code = "finance", IsEnabled = true, CreationTime = DateTime.UtcNow };
        await DbContext.FunctionModules.AddAsync(module);
        var a = NewFunction(module.Id, "finance.payrun.view");
        var x = NewFunction(module.Id, "finance.payrun.void");
        await DbContext.ModuleFunctions.AddRangeAsync(a, x);
        await DbContext.IdentityRoles.AddRangeAsync(
            new IdentityRole { Id = GrantorRoleId, Name = "Staff" },
            new IdentityRole { Id = PayrollRoleId, Name = "PayrollManager" });

        Grant(GrantorRoleId, a);
        if (grantorHoldsX)
        {
            Grant(GrantorRoleId, x);
        }
        Grant(PayrollRoleId, a);
        Grant(PayrollRoleId, x);
        await DbContext.SaveChangesAsync();
        return (a, x);
    }

    private void Grant(Guid roleId, ModuleFunction function)
        => DbContext.RoleFunctions.Add(new RoleFunction { Id = Guid.NewGuid(), RoleId = roleId, FunctionId = function.Id, IsEnabled = true });

    private static ModuleFunction NewFunction(Guid moduleId, string code) => new()
    {
        Id = Guid.NewGuid(), ModuleId = moduleId, Code = code, Name = code, IsEnabled = true, CreationTime = DateTime.UtcNow,
    };

    private async Task SetFunctionStateAsync(ModuleFunction function, bool? enabled = null, bool? retired = null)
    {
        DbContext.ChangeTracker.Clear();
        var row = DbContext.ModuleFunctions.Single(f => f.Id == function.Id);
        if (enabled.HasValue) row.IsEnabled = enabled.Value;
        if (retired.HasValue) row.IsRetired = retired.Value;
        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task A_disabled_grant_on_the_role_blocks_membership_changes_but_not_the_matrix()
    {
        var (_, x) = await SeedAsync();
        await SetFunctionStateAsync(x, enabled: false);
        var service = Service();

        (await service.CanManageRoleAsync(GrantorId, PayrollRoleId)).ShouldBeTrue("the matrix only weighs live codes, or a dormant grant would lock everyone out");
        (await service.CanManageRoleMembershipAsync(GrantorId, PayrollRoleId)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_retired_grant_on_the_role_blocks_membership_changes()
    {
        var (_, x) = await SeedAsync();
        await SetFunctionStateAsync(x, retired: true);

        (await Service().CanManageRoleMembershipAsync(GrantorId, PayrollRoleId)).ShouldBeFalse();
    }

    [Fact]
    public async Task With_every_grant_live_and_held_membership_changes_are_allowed()
    {
        await SeedAsync(grantorHoldsX: true);

        (await Service().CanManageRoleMembershipAsync(GrantorId, PayrollRoleId)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_live_grant_the_grantor_lacks_blocks_both_checks()
    {
        await SeedAsync();
        var service = Service();

        (await service.CanManageRoleAsync(GrantorId, PayrollRoleId)).ShouldBeFalse();
        (await service.CanManageRoleMembershipAsync(GrantorId, PayrollRoleId)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_super_admin_manages_membership_of_a_role_with_dormant_grants()
    {
        var (_, x) = await SeedAsync();
        await SetFunctionStateAsync(x, enabled: false);
        _grantorRoleNames = ["SuperAdmin"];

        (await Service().CanManageRoleMembershipAsync(GrantorId, PayrollRoleId)).ShouldBeTrue();
    }
}
