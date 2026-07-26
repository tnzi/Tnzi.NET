using Mapster;
using MapsterMapper;
using Tnzi.Mapster;
using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 超管短路 + deny-by-default 权限解析集成测试:真实 SQLite 仓储 +
/// 真实 <see cref="FunctionAuthorizationService"/>,只 mock 角色成员关系
/// (<see cref="IUserRoleService"/>)。覆盖:
/// <list type="bullet">
///   <item>超管绕过一切检查(含目录中不存在的码)并拿到全量目录;</item>
///   <item>非超管默认零权限(deny-by-default),仅显式授权(RoleFunction)生效;</item>
///   <item>禁用功能的显式授权不生效;权限名大小写不敏感;</item>
///   <item>UpdateModuleFunction 的 Category 元数据保护(漏传不降级/系统托管行不可改)。</item>
/// </list>
/// </summary>
public class SuperAdminAccessIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<IUserRoleService> _userRoleService = new();
    private readonly AuthOptions _authOptions = new();

    public SuperAdminAccessIntegrationTests()
    {
        // UpdateModuleFunctionAsync 走 request.MapTo(entity),测试进程需初始化
        // 全局 mapper(与 Finance/Audit 等集成测试同款样板)。
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<FunctionModule>(services);
        AddRepo<ModuleFunction>(services);
        AddRepo<RoleFunction>(services);
        AddRepo<UserFunction>(services);

        services.AddScoped(_ => _userRoleService.Object);
        // 每次解析读取当前 _authOptions,测试内可先改配置再取服务。
        services.AddScoped(_ => MsOptions.Create(_authOptions));

        services.AddScoped(sp => new FunctionAuthorizationService(
            sp.GetRequiredService<IRepository<FunctionModule, Guid>>(),
            sp.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
            sp.GetRequiredService<IRepository<RoleFunction, Guid>>(),
            sp.GetRequiredService<IRepository<UserFunction, Guid>>(),
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

    /// <summary>让 mock 的用户拥有指定角色名(超管判定用名称,显式授权用 RoleId)。</summary>
    private void SetUserRoles(params string[] roleNames)
    {
        _userRoleService
            .Setup(s => s.GetUserRolesAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(new Dictionary<Guid, IEnumerable<string>> { [UserId] = roleNames });
        _userRoleService
            .Setup(s => s.GetUserRoleIdsAsync(UserId))
            .ReturnsAsync(roleNames.Length > 0 ? new[] { RoleId } : Array.Empty<Guid>());
    }

    /// <summary>播种一个模块 + 一个 Business 码 + 一个 Technical 码。</summary>
    private async Task<(ModuleFunction Business, ModuleFunction Technical)> SeedCatalogueAsync()
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
        return (businessFn, technicalFn);
    }

    /// <summary>把功能显式授予测试角色(RoleFunction 直连)。</summary>
    private async Task GrantAsync(ModuleFunction function)
    {
        await DbContext.RoleFunctions.AddAsync(new RoleFunction
        {
            Id = Guid.NewGuid(), RoleId = RoleId, FunctionId = function.Id,
            IsEnabled = true, CreationTime = DateTime.UtcNow,
        });
        await DbContext.SaveChangesAsync();
    }

    private FunctionAuthorizationService GetService()
        => ServiceProvider.CreateScope().ServiceProvider.GetRequiredService<FunctionAuthorizationService>();

    [Fact]
    public async Task SuperAdmin_bypasses_all_codes_and_gets_full_catalogue()
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
    public async Task Non_super_admin_is_denied_by_default()
    {
        await SeedCatalogueAsync();
        _authOptions.SuperAdminRoles = ["SuperAdmin"];
        SetUserRoles("Admin");
        var service = GetService();

        // 无显式授权 = 零权限,不管码的 Category 是什么。
        (await service.CheckPermissionAsync(UserId, "user.view")).ShouldBeFalse();
        (await service.CheckPermissionAsync(UserId, "system.diagnostics.view")).ShouldBeFalse();
        (await service.CheckPermissionAsync(UserId, "no.such.code")).ShouldBeFalse();
        (await service.GetUserPermissionNamesAsync(UserId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Explicit_grant_gives_exactly_the_granted_code()
    {
        var (businessFn, technicalFn) = await SeedCatalogueAsync();
        _authOptions.SuperAdminRoles = ["SuperAdmin"];
        SetUserRoles("Admin");
        await GrantAsync(technicalFn);
        var service = GetService();

        (await service.CheckPermissionAsync(UserId, "system.diagnostics.view")).ShouldBeTrue();
        // 未授权的码仍拒绝——授权是逐码显式的,无任何隐式扩散。
        (await service.CheckPermissionAsync(UserId, businessFn.Code)).ShouldBeFalse();

        var names = (await service.GetUserPermissionNamesAsync(UserId)).ToList();
        names.ShouldBe(new[] { "system.diagnostics.view" });
    }

    [Fact]
    public async Task Grant_on_disabled_function_is_excluded()
    {
        var (_, _) = await SeedCatalogueAsync();
        var disabled = new ModuleFunction
        {
            Id = Guid.NewGuid(), Name = "View Payments", Code = "payment.view",
            ModuleId = DbContext.FunctionModules.First().Id,
            IsEnabled = false, Category = PermissionCategory.Business,
            CreationTime = DateTime.UtcNow,
        };
        await DbContext.ModuleFunctions.AddAsync(disabled);
        await DbContext.SaveChangesAsync();

        SetUserRoles("Admin");
        await GrantAsync(disabled);
        var service = GetService();

        (await service.CheckPermissionAsync(UserId, "payment.view")).ShouldBeFalse();
    }

    [Fact]
    public async Task Permission_names_are_case_insensitively_checked()
    {
        var (businessFn, _) = await SeedCatalogueAsync();
        SetUserRoles("Admin");
        await GrantAsync(businessFn);
        var service = GetService();

        (await service.CheckPermissionAsync(UserId, "USER.VIEW")).ShouldBeTrue();
    }

    [Fact]
    public async Task Super_admin_role_name_match_is_case_insensitive()
    {
        await SeedCatalogueAsync();
        _authOptions.SuperAdminRoles = ["superadmin"];
        SetUserRoles("SUPERADMIN");
        var service = GetService();

        (await service.CheckPermissionAsync(UserId, "user.view")).ShouldBeTrue();
    }

    [Fact]
    public async Task Super_admin_batch_check_mirrors_the_single_check_bypass()
    {
        await SeedCatalogueAsync();
        _authOptions.SuperAdminRoles = ["SuperAdmin"];
        SetUserRoles("SuperAdmin");
        var service = GetService();

        // Batch and single checks must reach the SAME verdict for a super
        // admin - including undeclared codes. Before the fix the batch path
        // resolved through the enabled-only catalogue, so an endpoint gate
        // (single) and an in-service RequireAllPermissionsAsync (batch) could
        // contradict each other for the same code.
        var verdicts = await service.CheckPermissionsAsync(
            UserId, new[] { "user.view", "system.diagnostics.view", "no.such.code" });

        verdicts["user.view"].ShouldBeTrue();
        verdicts["system.diagnostics.view"].ShouldBeTrue();
        verdicts["no.such.code"].ShouldBeTrue();
    }

    [Fact]
    public async Task Super_admin_role_protection_fails_closed_without_role_repository()
    {
        // This fixture builds the service WITHOUT a role repository. With
        // SuperAdminRoles configured, CanManageRoleAsync must treat every
        // role as protected - a super-admin role's explicit grant set is
        // usually empty, so silently dropping the name-based protection
        // would let any grantor "trivially dominate" it via the membership
        // path (self-escalation).
        var (businessFn, _) = await SeedCatalogueAsync();
        _authOptions.SuperAdminRoles = ["SuperAdmin"];
        SetUserRoles("Manager");
        await GrantAsync(businessFn);
        var service = GetService();

        (await service.CanManageRoleAsync(UserId, Guid.NewGuid())).ShouldBeFalse();
    }

    // ── UpdateModuleFunction 的 Category 元数据保护 ───────────────────────
    // Category 是分配界面的警示徽标元数据。请求 DTO 若带非空默认值(Business=0),
    // 任何漏传 category 的更新都会把 Technical 静默降级为 Business,抹掉徽标。

    [Fact]
    public async Task Update_without_category_preserves_technical_classification()
    {
        var (_, technicalFn) = await SeedCatalogueAsync();
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
        var (_, technicalFn) = await SeedCatalogueAsync();
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

    [Fact]
    public void GetSuperAdminRoleNames_returns_configured_names()
    {
        // The assignment UI renders these roles read-only - their members
        // bypass every check, so explicit RoleFunction rows are meaningless.
        var names = ((IRoleFunctionService)GetService()).GetSuperAdminRoleNames();

        names.ShouldBe(_authOptions.SuperAdminRoles);
    }
}
