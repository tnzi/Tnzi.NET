using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using IdentityRole = Tnzi.Identity.Entities.Role;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 委托授权护栏集成测试:真实 SQLite 仓储 + 真实
/// <see cref="FunctionAuthorizationService"/>,mock 角色成员关系。
/// 授权者 = 测试基类注册的 ICurrentUser(TestHelper.DefaultTestUserId)。覆盖:
/// <list type="bullet">
///   <item>非超管授权者只能授出自己持有的码(subset 约束,越界 403 且落库零行);</item>
///   <item>非超管授权者不能操作"权限集非自己子集"的强角色(assign/remove/set/clear 全拦);</item>
///   <item>非超管授权者不能操作超管配置角色(即使其显式授权为空);</item>
///   <item>超管绕过护栏;Clone/Import 同受 subset 约束;</item>
///   <item>CanManageRoleAsync 的支配语义(权限集包含模型)。</item>
/// </list>
/// </summary>
public class DelegationGuardIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    /// <summary>授权者 = 测试基类 ICurrentUser 的默认用户。</summary>
    private static readonly Guid GrantorId = TestHelper.DefaultTestUserId;
    private static readonly Guid GrantorRoleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TargetRoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Mock<IUserRoleService> _userRoleService = new();
    private readonly AuthOptions _authOptions = new();

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

    /// <summary>设置授权者的角色名(超管判定)与角色ID(显式授权路径)。</summary>
    private void SetGrantorRoles(string[] roleNames, params Guid[] roleIds)
    {
        _userRoleService
            .Setup(s => s.GetUserRolesAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(new Dictionary<Guid, IEnumerable<string>> { [GrantorId] = roleNames });
        _userRoleService
            .Setup(s => s.GetUserRoleIdsAsync(GrantorId))
            .ReturnsAsync(roleIds);
    }

    /// <summary>播种模块 + 三个功能码,返回 (held1, held2, notHeld)。</summary>
    private async Task<(ModuleFunction UserView, ModuleFunction RoleView, ModuleFunction Diagnostics)> SeedCatalogueAsync()
    {
        var module = new FunctionModule { Id = Guid.NewGuid(), Name = "System", Code = "system", CreationTime = DateTime.UtcNow };
        await DbContext.FunctionModules.AddAsync(module);

        var userView = NewFunction(module.Id, "View Users", "user.view");
        var roleView = NewFunction(module.Id, "View Roles", "role.view");
        var diagnostics = NewFunction(module.Id, "View Diagnostics", "system.diagnostics.view");
        await DbContext.ModuleFunctions.AddRangeAsync(userView, roleView, diagnostics);
        await DbContext.SaveChangesAsync();
        return (userView, roleView, diagnostics);
    }

    private static ModuleFunction NewFunction(Guid moduleId, string name, string code) => new()
    {
        Id = Guid.NewGuid(), Name = name, Code = code,
        ModuleId = moduleId, IsEnabled = true, CreationTime = DateTime.UtcNow,
    };

    private async Task GrantAsync(Guid roleId, params ModuleFunction[] functions)
    {
        foreach (var fn in functions)
        {
            await DbContext.RoleFunctions.AddAsync(new RoleFunction
            {
                Id = Guid.NewGuid(), RoleId = roleId, FunctionId = fn.Id,
                IsEnabled = true, CreationTime = DateTime.UtcNow,
            });
        }
        await DbContext.SaveChangesAsync();
    }

    private FunctionAuthorizationService GetService()
        => ServiceProvider.CreateScope().ServiceProvider.GetRequiredService<FunctionAuthorizationService>();

    /// <summary>标准布景:授权者持 user.view + role.view(经自己的角色),目标角色为空。</summary>
    private async Task<(ModuleFunction UserView, ModuleFunction RoleView, ModuleFunction Diagnostics)> SeedStandardAsync()
    {
        var fns = await SeedCatalogueAsync();
        SetGrantorRoles(["Manager"], GrantorRoleId);
        await GrantAsync(GrantorRoleId, fns.UserView, fns.RoleView);
        return fns;
    }

    [Fact]
    public async Task Grantor_can_assign_held_codes_to_dominated_role()
    {
        var fns = await SeedStandardAsync();
        var service = GetService();

        var result = await service.AssignFunctionsToRoleAsync(TargetRoleId, [fns.UserView.Id]);

        result.Succeeded.ShouldBeTrue(result.Message);
        DbContext.RoleFunctions.Count(rf => rf.RoleId == TargetRoleId).ShouldBe(1);
    }

    [Fact]
    public async Task Grantor_cannot_assign_codes_it_does_not_hold()
    {
        var fns = await SeedStandardAsync();
        var service = GetService();

        var result = await service.AssignFunctionsToRoleAsync(TargetRoleId, [fns.UserView.Id, fns.Diagnostics.Id]);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        result.Message.ShouldNotBeNull();
        result.Message!.ShouldContain("system.diagnostics.view");
        // 越界请求整体拒绝——包括其中本可授出的码,不做部分成功。
        DbContext.RoleFunctions.Count(rf => rf.RoleId == TargetRoleId).ShouldBe(0);
    }

    [Fact]
    public async Task Grantor_cannot_manage_role_stronger_than_itself()
    {
        var fns = await SeedStandardAsync();
        // 目标角色持有授权者没有的 diagnostics → 非授权者的支配对象。
        await GrantAsync(TargetRoleId, fns.Diagnostics);
        var service = GetService();

        (await service.AssignFunctionsToRoleAsync(TargetRoleId, [fns.UserView.Id])).Code.ShouldBe(403);
        (await service.RemoveFunctionsFromRoleAsync(TargetRoleId, [fns.Diagnostics.Id])).Code.ShouldBe(403);
        (await service.SetRoleFunctionsAsync(TargetRoleId, [fns.UserView.Id])).Code.ShouldBe(403);
        (await service.ClearRoleFunctionsAsync(TargetRoleId)).Code.ShouldBe(403);

        // 强角色的授权行原封不动。
        DbContext.RoleFunctions.Count(rf => rf.RoleId == TargetRoleId).ShouldBe(1);
    }

    [Fact]
    public async Task Grantor_cannot_manage_super_admin_role_even_with_empty_grants()
    {
        var fns = await SeedStandardAsync();
        _authOptions.SuperAdminRoles = ["SuperAdmin"];
        var superRole = new IdentityRole { Id = TargetRoleId, Name = "SuperAdmin", NormalizedName = "SUPERADMIN" };
        await DbContext.IdentityRoles.AddAsync(superRole);
        await DbContext.SaveChangesAsync();
        var service = GetService();

        // 超管角色显式授权为空(超管走 bypass 不需要授权行),若无专门保护
        // 会被任何人"平凡支配"——必须显式拒绝。
        var result = await service.AssignFunctionsToRoleAsync(TargetRoleId, [fns.UserView.Id]);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
    }

    [Fact]
    public async Task Super_admin_grantor_bypasses_all_guards()
    {
        var fns = await SeedCatalogueAsync();
        _authOptions.SuperAdminRoles = ["SuperAdmin"];
        SetGrantorRoles(["SuperAdmin"], GrantorRoleId);
        var service = GetService();

        var result = await service.AssignFunctionsToRoleAsync(TargetRoleId, [fns.Diagnostics.Id]);

        result.Succeeded.ShouldBeTrue(result.Message);
        DbContext.RoleFunctions.Count(rf => rf.RoleId == TargetRoleId).ShouldBe(1);
    }

    [Fact]
    public async Task Batch_assign_precheck_prevents_partial_commit()
    {
        var fns = await SeedStandardAsync();
        var dominatedRoleId = Guid.NewGuid();
        // 目标角色 2 持有授权者没有的 diagnostics → 非支配对象,护栏必拒。
        var strongerRoleId = Guid.NewGuid();
        await GrantAsync(strongerRoleId, fns.Diagnostics);
        var service = GetService();

        var result = await service.BatchAssignFunctionsAsync(
            [dominatedRoleId, strongerRoleId], [fns.UserView.Id]);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        // 全批拒绝:第一个(可支配的)角色也不得留下半批写入。
        DbContext.RoleFunctions.Count(rf => rf.RoleId == dominatedRoleId).ShouldBe(0);
        DbContext.RoleFunctions.Count(rf => rf.RoleId == strongerRoleId).ShouldBe(1);
    }

    [Fact]
    public async Task Clone_respects_subset_constraint()
    {
        var fns = await SeedStandardAsync();
        var sourceRoleId = Guid.NewGuid();
        await GrantAsync(sourceRoleId, fns.Diagnostics);
        var service = GetService();

        var result = await service.CloneRoleFunctionsAsync(sourceRoleId, TargetRoleId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        DbContext.RoleFunctions.Count(rf => rf.RoleId == TargetRoleId).ShouldBe(0);
    }

    [Fact]
    public async Task Import_respects_subset_constraint()
    {
        var fns = await SeedStandardAsync();
        var service = GetService();

        var result = await service.ImportRolePermissionsAsync(TargetRoleId, new RolePermissionExportDto
        {
            Version = "1.0",
            ExportedAt = DateTime.UtcNow,
            SourceRoleId = Guid.NewGuid(),
            FunctionCodes = [fns.Diagnostics.Code],
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        DbContext.RoleFunctions.Count(rf => rf.RoleId == TargetRoleId).ShouldBe(0);
    }

    [Fact]
    public async Task CanManageRole_reflects_permission_set_containment()
    {
        var fns = await SeedStandardAsync();
        var dominatedRoleId = Guid.NewGuid();
        await GrantAsync(dominatedRoleId, fns.UserView);
        var strongerRoleId = Guid.NewGuid();
        await GrantAsync(strongerRoleId, fns.Diagnostics);
        var service = GetService();

        (await service.CanManageRoleAsync(GrantorId, dominatedRoleId)).ShouldBeTrue();
        (await service.CanManageRoleAsync(GrantorId, strongerRoleId)).ShouldBeFalse();
        // 空角色是任何授权者的支配对象(空集是一切集合的子集)。
        (await service.CanManageRoleAsync(GrantorId, Guid.NewGuid())).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAccessProfile_returns_backend_authoritative_flag_and_codes()
    {
        var fns = await SeedStandardAsync();
        var service = GetService();

        var profile = await service.GetAccessProfileAsync(GrantorId);
        profile.Succeeded.ShouldBeTrue(profile.Message);
        profile.Data!.IsSuperAdmin.ShouldBeFalse();
        profile.Data.Permissions.ShouldBe(
            new[] { fns.UserView.Code, fns.RoleView.Code }, ignoreOrder: true);

        _authOptions.SuperAdminRoles = ["Manager"];
        var superProfile = await service.GetAccessProfileAsync(GrantorId);
        superProfile.Data!.IsSuperAdmin.ShouldBeTrue();
        superProfile.Data.Permissions.ShouldContain(fns.Diagnostics.Code);
    }
}
