using Mapster;
using MapsterMapper;
using Tnzi.Mapster;
using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 两档管理员(SuperAdmin / BusinessAdmin)权限解析集成测试:真实 SQLite 仓储 +
/// 真实 <see cref="FunctionAuthorizationService"/>,只 mock 角色成员关系
/// (<see cref="IUserRoleService"/>)。覆盖:
/// <list type="bullet">
///   <item>超管绕过一切(含 Technical 码)并拿到全量目录;</item>
///   <item>业务管理员隐式获得全部 Business 码、被 Technical 码拒绝;</item>
///   <item>业务管理员的显式授权(RoleFunction)可叠加 Technical 码;</item>
///   <item>同角色出现在两档时按超管解析;</item>
///   <item>普通用户仅显式授权;禁用功能不进业务目录。</item>
/// </list>
/// </summary>
public class AdminTierIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<IUserRoleService> _userRoleService = new();
    private readonly AuthOptions _authOptions = new();

    public AdminTierIntegrationTests()
    {
        // UpdateModuleFunctionAsync 走 request.MapTo(entity),测试进程需初始化
        // 全局 mapper(与 Finance/Audit 等集成测试同款样板)。
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<FunctionModule>(services);
        AddRepo<ModuleFunction>(services);
        AddRepo<ModuleUser>(services);
        AddRepo<ModuleRole>(services);
        AddRepo<RoleFunction>(services);

        services.AddScoped(_ => _userRoleService.Object);
        // 每次解析读取当前 _authOptions,测试内可先改配置再取服务。
        services.AddScoped(_ => MsOptions.Create(_authOptions));

        services.AddScoped(sp => new FunctionAuthorizationService(
            sp.GetRequiredService<IRepository<FunctionModule, Guid>>(),
            sp.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
            sp.GetRequiredService<IRepository<ModuleUser, Guid>>(),
            sp.GetRequiredService<IRepository<ModuleRole, Guid>>(),
            sp.GetRequiredService<IRepository<RoleFunction, Guid>>(),
            sp,
            sp.GetRequiredService<IUserRoleService>(),
            functionAuthCache: null,
            options: sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>()));
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<AuthorizationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<AuthorizationTestDbContext>(), serviceProvider: sp));
    }

    /// <summary>让 mock 的用户拥有指定角色名(两档判定用名称,显式授权用 RoleId)。</summary>
    private void SetUserRoles(params string[] roleNames)
    {
        _userRoleService
            .Setup(s => s.GetUserRolesAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(new Dictionary<Guid, IEnumerable<string>> { [UserId] = roleNames });
        _userRoleService
            .Setup(s => s.GetUserRoleIdsAsync(UserId))
            .ReturnsAsync(roleNames.Length > 0 ? new[] { RoleId } : Array.Empty<Guid>());
    }

    /// <summary>播种一个模块 + 一个 Business 码 + 一个 Technical 码,返回 Technical 功能。</summary>
    private async Task<ModuleFunction> SeedCatalogueAsync()
    {
        var module = new FunctionModule { Id = Guid.NewGuid(), Name = "System", Code = "system", CreationTime = DateTime.UtcNow };
        await DbContext.FunctionModules.AddAsync(module);

        var businessFn = new ModuleFunction
        {
            Id = Guid.NewGuid(), Name = "View Users", Code = "user.view",
            ModuleId = module.Id, IsEnabled = true, Category = PermissionCategory.Business,
            CreationTime = DateTime.UtcNow,
        };
        var technicalFn = new ModuleFunction
        {
            Id = Guid.NewGuid(), Name = "View Diagnostics", Code = "system.diagnostics.view",
            ModuleId = module.Id, IsEnabled = true, Category = PermissionCategory.Technical,
            CreationTime = DateTime.UtcNow,
        };
        await DbContext.ModuleFunctions.AddRangeAsync(businessFn, technicalFn);
        await DbContext.SaveChangesAsync();
        return technicalFn;
    }

    private FunctionAuthorizationService GetService()
        => ServiceProvider.CreateScope().ServiceProvider.GetRequiredService<FunctionAuthorizationService>();

    [Fact]
    public async Task SuperAdmin_bypasses_technical_codes_and_gets_full_catalogue()
    {
        await SeedCatalogueAsync();
        _authOptions.SuperAdminRoles = ["SuperAdmin"];
        SetUserRoles("SuperAdmin");
        var service = GetService();

        (await service.CheckPermissionAsync(UserId, "user.view")).ShouldBeTrue();
        (await service.CheckPermissionAsync(UserId, "system.diagnostics.view")).ShouldBeTrue();
        // 超管绕过甚至不要求码存在于目录。
        (await service.CheckPermissionAsync(UserId, "no.such.code")).ShouldBeTrue();

        var names = (await service.GetUserPermissionNamesAsync(UserId)).ToList();
        names.ShouldContain("user.view");
        names.ShouldContain("system.diagnostics.view");
    }

    [Fact]
    public async Task BusinessAdmin_gets_business_catalogue_but_not_technical()
    {
        await SeedCatalogueAsync();
        _authOptions.BusinessAdminRoles = ["Admin"];
        SetUserRoles("Admin");
        var service = GetService();

        (await service.CheckPermissionAsync(UserId, "user.view")).ShouldBeTrue();
        (await service.CheckPermissionAsync(UserId, "system.diagnostics.view")).ShouldBeFalse();
        (await service.CheckPermissionAsync(UserId, "no.such.code")).ShouldBeFalse();

        var names = (await service.GetUserPermissionNamesAsync(UserId)).ToList();
        names.ShouldContain("user.view");
        names.ShouldNotContain("system.diagnostics.view");
    }

    [Fact]
    public async Task BusinessAdmin_explicit_grant_adds_technical_code_on_top()
    {
        var technicalFn = await SeedCatalogueAsync();
        _authOptions.BusinessAdminRoles = ["Admin"];
        SetUserRoles("Admin");

        // 显式把 Technical 功能授给该角色(RoleFunction 直连)。
        await DbContext.RoleFunctions.AddAsync(new RoleFunction
        {
            Id = Guid.NewGuid(), RoleId = RoleId, FunctionId = technicalFn.Id,
            IsEnabled = true, CreationTime = DateTime.UtcNow,
        });
        await DbContext.SaveChangesAsync();
        var service = GetService();

        (await service.CheckPermissionAsync(UserId, "system.diagnostics.view")).ShouldBeTrue();

        var names = (await service.GetUserPermissionNamesAsync(UserId)).ToList();
        names.ShouldContain("user.view");
        names.ShouldContain("system.diagnostics.view");
    }

    [Fact]
    public async Task Role_listed_in_both_tiers_resolves_as_super_admin()
    {
        await SeedCatalogueAsync();
        _authOptions.SuperAdminRoles = ["Admin"];
        _authOptions.BusinessAdminRoles = ["Admin"];
        SetUserRoles("Admin");
        var service = GetService();

        (await service.CheckPermissionAsync(UserId, "system.diagnostics.view")).ShouldBeTrue();
    }

    [Fact]
    public async Task Regular_user_gets_explicit_grants_only()
    {
        await SeedCatalogueAsync();
        _authOptions.SuperAdminRoles = ["SuperAdmin"];
        _authOptions.BusinessAdminRoles = ["Admin"];
        SetUserRoles("Editor");
        var service = GetService();

        (await service.CheckPermissionAsync(UserId, "user.view")).ShouldBeFalse();
        (await service.CheckPermissionAsync(UserId, "system.diagnostics.view")).ShouldBeFalse();
        (await service.GetUserPermissionNamesAsync(UserId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Disabled_business_function_is_excluded_from_business_catalogue()
    {
        await SeedCatalogueAsync();
        var disabled = new ModuleFunction
        {
            Id = Guid.NewGuid(), Name = "View Payments", Code = "payment.view",
            ModuleId = DbContext.FunctionModules.First().Id,
            IsEnabled = false, Category = PermissionCategory.Business,
            CreationTime = DateTime.UtcNow,
        };
        await DbContext.ModuleFunctions.AddAsync(disabled);
        await DbContext.SaveChangesAsync();

        _authOptions.BusinessAdminRoles = ["Admin"];
        SetUserRoles("Admin");
        var service = GetService();

        (await service.CheckPermissionAsync(UserId, "payment.view")).ShouldBeFalse();
    }

    [Fact]
    public async Task Business_admin_permission_names_are_case_insensitively_checked()
    {
        await SeedCatalogueAsync();
        _authOptions.BusinessAdminRoles = ["admin"];
        SetUserRoles("ADMIN");
        var service = GetService();

        (await service.CheckPermissionAsync(UserId, "USER.VIEW")).ShouldBeTrue();
    }

    // ── UpdateModuleFunction 的 Category 保护(提权回归锁) ─────────────────
    // 评审发现:请求 DTO 若带非空默认值(Business=0),任何漏传 category 的
    // 更新都会把 Technical 静默降级为 Business,业务管理员立即隐式获得该权限。

    [Fact]
    public async Task Update_without_category_preserves_technical_classification()
    {
        var technicalFn = await SeedCatalogueAsync();
        var service = GetService();

        var result = await service.UpdateModuleFunctionAsync(technicalFn.Id, new UpdateModuleFunctionRequest
        {
            Name = "View Diagnostics (renamed)",
            Code = technicalFn.Code,
            ModuleId = technicalFn.ModuleId,
            Order = 5,
            // Category 未提供(null)——绝不能掉回 Business。
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Category.ShouldBe(PermissionCategory.Technical);
    }

    [Fact]
    public async Task Update_with_explicit_category_changes_admin_created_function()
    {
        var technicalFn = await SeedCatalogueAsync();
        var service = GetService();

        var result = await service.UpdateModuleFunctionAsync(technicalFn.Id, new UpdateModuleFunctionRequest
        {
            Name = technicalFn.Name,
            Code = technicalFn.Code,
            ModuleId = technicalFn.ModuleId,
            Category = PermissionCategory.Business,
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Category.ShouldBe(PermissionCategory.Business);
    }

    [Fact]
    public async Task Update_cannot_flip_category_on_system_managed_function()
    {
        await SeedCatalogueAsync();
        var managed = new ModuleFunction
        {
            Id = Guid.NewGuid(), Name = "Execute AI SQL", Code = "ai.sql.execute",
            ModuleId = DbContext.FunctionModules.First().Id,
            IsEnabled = true, Category = PermissionCategory.Technical,
            IsSystemManaged = true, CreationTime = DateTime.UtcNow,
        };
        await DbContext.ModuleFunctions.AddAsync(managed);
        await DbContext.SaveChangesAsync();
        var service = GetService();

        var result = await service.UpdateModuleFunctionAsync(managed.Id, new UpdateModuleFunctionRequest
        {
            Name = managed.Name,
            Code = managed.Code,
            ModuleId = managed.ModuleId,
            // 显式尝试降级——系统托管行的分类是 code-owned,必须被忽略。
            Category = PermissionCategory.Business,
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Category.ShouldBe(PermissionCategory.Technical);
    }
}
