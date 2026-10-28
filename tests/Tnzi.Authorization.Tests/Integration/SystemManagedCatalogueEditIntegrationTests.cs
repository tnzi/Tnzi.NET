using Mapster;
using MapsterMapper;
using Tnzi.Mapster;
using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 系统托管的模块与功能（由 <c>IPermissionDefinitionProvider</c> 声明、播种器每次启动按声明改回）
/// 只有排序可经管理端改动，其余由代码声明的字段一律 403。真实 SQLite 仓储。
/// </summary>
/// <remarks>
/// ★ 此前只拦码（模块）与码 + 所属模块（功能）：名称、说明、父模块照写、答 200、立即生效，
/// 下次启动被播种器悄悄改回，管理员看到的是「改动丢了」而没有任何错误。
/// 判定按值比较而不是按字段是否出现：管理端提交的是整张表单。
/// </remarks>
public class SystemManagedCatalogueEditIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private static readonly Guid ParentModuleId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid SystemModuleId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid CustomModuleId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003");
    private static readonly Guid SystemFunctionId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000011");
    private static readonly Guid CustomFunctionId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000012");

    private readonly AuthOptions _authOptions = new();

    public SystemManagedCatalogueEditIntegrationTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<FunctionModule>(services);
        AddRepo<ModuleFunction>(services);
        AddRepo<RoleFunction>(services);
        AddRepo<UserFunction>(services);
        services.AddScoped(_ => MsOptions.Create(_authOptions));

        services.AddScoped(sp => new FunctionAuthorizationService(
            sp.GetRequiredService<IRepository<FunctionModule, Guid>>(),
            sp.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
            sp.GetRequiredService<IRepository<RoleFunction, Guid>>(),
            sp.GetRequiredService<IRepository<UserFunction, Guid>>(),
            sp,
            userRoleService: null,
            functionAuthCache: null,
            options: sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>()));
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<AuthorizationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<AuthorizationTestDbContext>(), serviceProvider: sp));
    }

    private FunctionAuthorizationService Service()
        => ServiceProvider.CreateScope().ServiceProvider.GetRequiredService<FunctionAuthorizationService>();

    private async Task SeedAsync()
    {
        DbContext.FunctionModules.AddRange(
            new FunctionModule { Id = ParentModuleId, Code = "custom.parent", Name = "Custom Parent", IsEnabled = true, CreationTime = DateTime.UtcNow },
            new FunctionModule
            {
                Id = SystemModuleId, Code = "identity", Name = "Identity", Description = null, Order = 1,
                IsEnabled = true, IsSystemManaged = true, CreationTime = DateTime.UtcNow,
            },
            new FunctionModule { Id = CustomModuleId, Code = "custom", Name = "Custom", IsEnabled = true, CreationTime = DateTime.UtcNow });
        DbContext.ModuleFunctions.AddRange(
            new ModuleFunction
            {
                Id = SystemFunctionId, ModuleId = SystemModuleId, Code = "identity.user.view", Name = "View Users",
                Description = "Read the user list", Order = 1, IsEnabled = true, IsSystemManaged = true, CreationTime = DateTime.UtcNow,
            },
            new ModuleFunction
            {
                Id = CustomFunctionId, ModuleId = CustomModuleId, Code = "custom.thing.view", Name = "View Things",
                Order = 1, IsEnabled = true, CreationTime = DateTime.UtcNow,
            });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>与库里那一行逐字相同的整张表单（管理端提交的就是这个形状）。</summary>
    private static UpdateFunctionModuleRequest SystemModuleForm()
        => new() { Code = "identity", Name = "Identity", Description = null, ParentId = null, Order = 1 };

    private static UpdateModuleFunctionRequest SystemFunctionForm()
        => new() { Code = "identity.user.view", Name = "View Users", Description = "Read the user list", ModuleId = SystemModuleId, Order = 1 };

    private async Task<FunctionModule> ReloadModuleAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.FunctionModules.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private async Task<ModuleFunction> ReloadFunctionAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.ModuleFunctions.AsNoTracking().SingleAsync(f => f.Id == id);
    }

    // ── 模块 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Renaming_a_system_managed_module_is_refused_and_leaves_the_row_alone()
    {
        await SeedAsync();
        var form = SystemModuleForm();
        form.Name = "Accounts & Security";

        var result = await Service().UpdateModuleAsync(SystemModuleId, form);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        (await ReloadModuleAsync(SystemModuleId)).Name.ShouldBe("Identity");
    }

    [Fact]
    public async Task Changing_a_system_managed_module_description_is_refused()
    {
        await SeedAsync();
        var form = SystemModuleForm();
        form.Description = "Edited by an admin";

        var result = await Service().UpdateModuleAsync(SystemModuleId, form);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        (await ReloadModuleAsync(SystemModuleId)).Description.ShouldBeNull();
    }

    [Fact]
    public async Task Moving_a_system_managed_module_under_another_module_is_refused()
    {
        await SeedAsync();
        var form = SystemModuleForm();
        form.ParentId = ParentModuleId;

        var result = await Service().UpdateModuleAsync(SystemModuleId, form);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        (await ReloadModuleAsync(SystemModuleId)).ParentId.ShouldBeNull();
    }

    [Fact]
    public async Task Reordering_a_system_managed_module_is_allowed()
    {
        await SeedAsync();
        var form = SystemModuleForm();
        form.Order = 9;

        var result = await Service().UpdateModuleAsync(SystemModuleId, form);

        result.Succeeded.ShouldBeTrue(result.Message);
        (await ReloadModuleAsync(SystemModuleId)).Order.ShouldBe(9);
    }

    [Fact]
    public async Task A_blank_description_on_a_system_managed_module_counts_as_unchanged()
    {
        // 表单把 null 说明渲染成空输入框，回传的可能是空串或空白。
        await SeedAsync();
        var form = SystemModuleForm();
        form.Description = "  ";
        form.Name = " Identity ";

        var result = await Service().UpdateModuleAsync(SystemModuleId, form);

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task A_custom_module_can_still_be_renamed_and_moved()
    {
        await SeedAsync();

        var result = await Service().UpdateModuleAsync(CustomModuleId, new UpdateFunctionModuleRequest
        {
            Code = "custom", Name = "Renamed", Description = "Now described", ParentId = ParentModuleId, Order = 2,
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        var row = await ReloadModuleAsync(CustomModuleId);
        row.Name.ShouldBe("Renamed");
        row.Description.ShouldBe("Now described");
        row.ParentId.ShouldBe(ParentModuleId);
    }

    // ── 功能 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Renaming_a_system_managed_function_is_refused_and_leaves_the_row_alone()
    {
        await SeedAsync();
        var form = SystemFunctionForm();
        form.Name = "See Users";

        var result = await Service().UpdateModuleFunctionAsync(SystemFunctionId, form);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        (await ReloadFunctionAsync(SystemFunctionId)).Name.ShouldBe("View Users");
    }

    [Fact]
    public async Task Changing_a_system_managed_function_description_is_refused()
    {
        await SeedAsync();
        var form = SystemFunctionForm();
        form.Description = null;

        var result = await Service().UpdateModuleFunctionAsync(SystemFunctionId, form);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        (await ReloadFunctionAsync(SystemFunctionId)).Description.ShouldBe("Read the user list");
    }

    [Fact]
    public async Task Reordering_a_system_managed_function_is_allowed()
    {
        await SeedAsync();
        var form = SystemFunctionForm();
        form.Order = 7;

        var result = await Service().UpdateModuleFunctionAsync(SystemFunctionId, form);

        result.Succeeded.ShouldBeTrue(result.Message);
        (await ReloadFunctionAsync(SystemFunctionId)).Order.ShouldBe(7);
    }

    [Fact]
    public async Task A_custom_function_can_still_be_renamed()
    {
        await SeedAsync();

        var result = await Service().UpdateModuleFunctionAsync(CustomFunctionId, new UpdateModuleFunctionRequest
        {
            Code = "custom.thing.view", Name = "See Things", Description = "Described", ModuleId = CustomModuleId, Order = 1,
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        var row = await ReloadFunctionAsync(CustomFunctionId);
        row.Name.ShouldBe("See Things");
        row.Description.ShouldBe("Described");
    }
}
