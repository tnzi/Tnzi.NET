using Mapster;
using MapsterMapper;
using Tnzi.Mapster;
using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using IdentityRole = Tnzi.Identity.Entities.Role;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 权限码与模块码的唯一性按大小写不敏感判定，与运行时认码同一口径。真实 SQLite（区分大小写的比较与唯一索引）。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 运行时、播种器、持有者反查都按大小写不敏感认码，而管理端判重此前是 SQL 的 <c>==</c>：
/// 在 PostgreSQL / SQLite 上建得出与系统码只差大小写的行。两条后果：
/// 播种器把它们收进不区分大小写的字典时抛异常、此后每次启动整轮跳过（新码永不入库）；
/// 把自己持有的自建码改名成 <c>User.Delete</c>，运行时就按 <c>user.delete</c> 放行，
/// 绕过「只能授出自己持有的码」，拿到任意权限码。
/// </para>
/// <para>
/// 已经在库里的只差大小写的两行（修复之前建出来的）由播种器容错、由否定行按大小写不敏感扣除。
/// </para>
/// </remarks>
public class PermissionCodeCaseIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private static readonly Guid CallerId = TestHelper.DefaultTestUserId;
    private static readonly Guid CallerRoleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TargetUserId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid TargetRoleId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private readonly Mock<IUserRoleService> _userRoleService = new();
    private readonly AuthOptions _authOptions = new() { SuperAdminRoles = ["SuperAdmin"] };
    private readonly Dictionary<Guid, string[]> _roleNamesByUser = new();
    private readonly Dictionary<Guid, Guid[]> _roleIdsByUser = new();

    public PermissionCodeCaseIntegrationTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        _userRoleService
            .Setup(s => s.GetUserRolesAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync((IEnumerable<Guid> ids) => ids
                .Where(_roleNamesByUser.ContainsKey)
                .ToDictionary(id => id, id => (IEnumerable<string>)_roleNamesByUser[id]));
        _userRoleService
            .Setup(s => s.GetUserRoleIdsAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => _roleIdsByUser.TryGetValue(id, out var roleIds) ? roleIds : Array.Empty<Guid>());
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

    private async Task<FunctionModule> SeedModuleAsync(string code)
    {
        var module = new FunctionModule { Id = Guid.NewGuid(), Name = code, Code = code, IsEnabled = true, IsSystemManaged = true, CreationTime = DateTime.UtcNow };
        await DbContext.FunctionModules.AddAsync(module);
        await DbContext.SaveChangesAsync();
        return module;
    }

    private async Task<ModuleFunction> SeedFunctionAsync(Guid moduleId, string code, bool systemManaged)
    {
        var function = new ModuleFunction
        {
            Id = Guid.NewGuid(), ModuleId = moduleId, Code = code, Name = code,
            IsEnabled = true, IsSystemManaged = systemManaged, CreationTime = DateTime.UtcNow,
        };
        await DbContext.ModuleFunctions.AddAsync(function);
        await DbContext.SaveChangesAsync();
        return function;
    }

    /// <summary>调用者是普通管理员，经自己的角色持有给定的功能。</summary>
    private async Task MakeCallerRegularWithAsync(params ModuleFunction[] held)
    {
        _roleNamesByUser[CallerId] = ["Staff"];
        _roleIdsByUser[CallerId] = [CallerRoleId];
        foreach (var fn in held)
        {
            await DbContext.RoleFunctions.AddAsync(new RoleFunction { Id = Guid.NewGuid(), RoleId = CallerRoleId, FunctionId = fn.Id, IsEnabled = true });
        }
        await DbContext.SaveChangesAsync();
    }

    // ── 判重 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Creating_a_function_whose_code_differs_only_by_case_is_refused()
    {
        var module = await SeedModuleAsync("identity");
        await SeedFunctionAsync(module.Id, "user.delete", systemManaged: true);

        var result = await Service().CreateModuleFunctionAsync(new CreateModuleFunctionRequest { Code = "User.Delete", Name = "Delete", ModuleId = module.Id });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        DbContext.ModuleFunctions.Count(f => f.Code.ToLower() == "user.delete").ShouldBe(1);
    }

    [Fact]
    public async Task Creating_a_module_whose_code_differs_only_by_case_is_refused()
    {
        await SeedModuleAsync("identity");

        var result = await Service().CreateModuleAsync(new CreateFunctionModuleRequest { Code = "Identity", Name = "Shadow" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        DbContext.FunctionModules.Count(m => m.Code.ToLower() == "identity").ShouldBe(1);
    }

    /// <summary>
    /// 提权路径本身：持有一个自建码的管理员把它改名成系统码的大小写变体，运行时就按系统码放行。
    /// </summary>
    [Fact]
    public async Task Renaming_a_held_function_to_a_case_variant_of_another_code_is_refused()
    {
        var module = await SeedModuleAsync("identity");
        await SeedFunctionAsync(module.Id, "user.delete", systemManaged: true);
        var custom = await SeedFunctionAsync(module.Id, "custom.report", systemManaged: false);
        await MakeCallerRegularWithAsync(custom);
        var service = Service();

        var result = await service.UpdateModuleFunctionAsync(custom.Id, new UpdateModuleFunctionRequest { Code = "User.Delete", Name = "x", ModuleId = module.Id });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        (await service.CheckPermissionAsync(CallerId, "user.delete")).ShouldBeFalse();
    }

    /// <summary>
    /// 改码 = 把新码授给这一行的每一个持有者，不经「只能授出自己持有的码」那道护栏：非超管不能改码。
    /// </summary>
    [Fact]
    public async Task A_non_super_admin_cannot_change_a_function_code()
    {
        var module = await SeedModuleAsync("custom");
        var custom = await SeedFunctionAsync(module.Id, "custom.report", systemManaged: false);
        await MakeCallerRegularWithAsync(custom);

        var result = await Service().UpdateModuleFunctionAsync(custom.Id, new UpdateModuleFunctionRequest { Code = "finance.payrun.void", Name = "x", ModuleId = module.Id });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        DbContext.ChangeTracker.Clear();
        DbContext.ModuleFunctions.Single(f => f.Id == custom.Id).Code.ShouldBe("custom.report");
    }

    [Fact]
    public async Task A_super_admin_can_change_a_function_code_and_anyone_can_change_only_its_case()
    {
        var module = await SeedModuleAsync("custom");
        var custom = await SeedFunctionAsync(module.Id, "custom.report", systemManaged: false);
        await MakeCallerRegularWithAsync(custom);

        // 只改大小写不改变任何人的权限（码处处大小写不敏感），普通管理员照常可以。
        (await Service().UpdateModuleFunctionAsync(custom.Id, new UpdateModuleFunctionRequest { Code = "Custom.Report", Name = "x", ModuleId = module.Id }))
            .Succeeded.ShouldBeTrue();

        _roleNamesByUser[CallerId] = ["SuperAdmin"];
        (await Service().UpdateModuleFunctionAsync(custom.Id, new UpdateModuleFunctionRequest { Code = "custom.report.v2", Name = "x", ModuleId = module.Id }))
            .Succeeded.ShouldBeTrue();
    }

    // ── 修复之前已经在库里的只差大小写的两行 ─────────────────────────────────────

    /// <summary>
    /// 否定行要扣掉码的每一个大小写变体：SQL 的 EXCEPT 按数据库排序规则比较，对 <c>user.delete</c> 的否定扣不掉 <c>User.Delete</c>。
    /// </summary>
    [Fact]
    public async Task A_user_level_deny_removes_every_case_variant_of_the_code()
    {
        var module = await SeedModuleAsync("identity");
        var system = await SeedFunctionAsync(module.Id, "user.delete", systemManaged: true);
        var legacyVariant = await SeedFunctionAsync(module.Id, "User.Delete", systemManaged: false);

        _roleIdsByUser[TargetUserId] = [TargetRoleId];
        await DbContext.RoleFunctions.AddAsync(new RoleFunction { Id = Guid.NewGuid(), RoleId = TargetRoleId, FunctionId = legacyVariant.Id, IsEnabled = true });
        await DbContext.UserFunctions.AddAsync(new UserFunction { Id = Guid.NewGuid(), UserId = TargetUserId, FunctionId = system.Id, IsGranted = false, IsEnabled = true });
        await DbContext.SaveChangesAsync();

        (await Service().CheckPermissionAsync(TargetUserId, "user.delete")).ShouldBeFalse();
    }

    private sealed class CatalogProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("catalog", "Catalog");
            context.AddPermission("catalog.product.view", "View", parentName: "catalog");
            context.AddPermission("catalog.product.export", "Export", parentName: "catalog");
        }
    }

    /// <summary>
    /// 播种器遇到只差大小写的两行不能整轮崩掉：此前那一轮（以及之后每一次启动）新声明的码都进不了库。
    /// </summary>
    [Fact]
    public async Task Seeding_survives_case_variant_rows_and_still_inserts_new_codes()
    {
        var module = await SeedModuleAsync("catalog");
        await DbContext.FunctionModules.AddAsync(new FunctionModule { Id = Guid.NewGuid(), Name = "Shadow", Code = "Catalog", IsEnabled = true, CreationTime = DateTime.UtcNow.AddMinutes(1) });
        await SeedFunctionAsync(module.Id, "catalog.product.view", systemManaged: true);
        var adminVariant = await SeedFunctionAsync(module.Id, "Catalog.Product.View", systemManaged: false);

        var seeder = new PermissionDbSeeder(
            ServiceProvider.GetRequiredService<IRepository<FunctionModule, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PermissionDbSeeder>.Instance,
            MsOptions.Create(_authOptions),
            ServiceProvider.GetRequiredService<IRepository<RoleFunction, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<UserFunction, Guid>>());

        await seeder.SeedAsync([new CatalogProvider()]);

        DbContext.ChangeTracker.Clear();
        var export = DbContext.ModuleFunctions.SingleOrDefault(f => f.Code == "catalog.product.export");
        export.ShouldNotBeNull();
        export.ModuleId.ShouldBe(module.Id);
        // 被遮住的那一行原样留着，交人工清理（不被认领为系统托管、不被退役）。
        var shadowed = DbContext.ModuleFunctions.Single(f => f.Id == adminVariant.Id);
        shadowed.IsSystemManaged.ShouldBeFalse();
        shadowed.IsRetired.ShouldBeFalse();
    }
}
