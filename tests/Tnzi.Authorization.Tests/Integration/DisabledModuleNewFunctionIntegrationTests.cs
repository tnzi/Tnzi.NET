using Mapster;
using MapsterMapper;
using Tnzi.Mapster;
using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 停用一个模块是一次级联操作（模块与其下全部功能一起停用），不是生效判据：运行时只看功能自己的启用与退役。
/// 所以停用期间新出现的功能必须随模块一起停用，否则停用模块下会冒出一个生效的码。真实 SQLite 仓储。
/// </summary>
/// <remarks>
/// 两条入口：播种器为新声明的码插行，管理端新建功能。此前两者都无条件按启用插入：
/// 运维在事故中停用 finance 模块，下一次部署新增的 finance 码却以启用状态出现在这个停用模块下。
/// </remarks>
public class DisabledModuleNewFunctionIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private readonly AuthOptions _authOptions = new();

    public DisabledModuleNewFunctionIntegrationTests()
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

    /// <summary>第一次部署：demo 模块下一个码。</summary>
    private sealed class FirstRelease : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("demo", "Demo");
            context.AddPermission("demo.thing.view", "View", parentName: "demo");
        }
    }

    /// <summary>下一次部署：同一模块新增一个码。</summary>
    private sealed class SecondRelease : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("demo", "Demo");
            context.AddPermission("demo.thing.view", "View", parentName: "demo");
            context.AddPermission("demo.thing.export", "Export", parentName: "demo");
        }
    }

    private PermissionDbSeeder CreateSeeder()
    {
        var scope = ServiceProvider.CreateScope().ServiceProvider;
        return new PermissionDbSeeder(
            scope.GetRequiredService<IRepository<FunctionModule, Guid>>(),
            scope.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PermissionDbSeeder>.Instance,
            MsOptions.Create(_authOptions),
            scope.GetRequiredService<IRepository<RoleFunction, Guid>>(),
            scope.GetRequiredService<IRepository<UserFunction, Guid>>());
    }

    private FunctionAuthorizationService Service()
        => ServiceProvider.CreateScope().ServiceProvider.GetRequiredService<FunctionAuthorizationService>();

    private async Task<Guid> DemoModuleIdAsync()
        => (await DbContext.FunctionModules.AsNoTracking().SingleAsync(m => m.Code == "demo")).Id;

    private async Task<ModuleFunction> FunctionAsync(string code)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.ModuleFunctions.AsNoTracking().SingleAsync(f => f.Code == code);
    }

    [Fact]
    public async Task A_code_first_declared_while_its_module_is_disabled_is_seeded_disabled()
    {
        await CreateSeeder().SeedAsync([new FirstRelease()]);
        (await Service().DisableModuleAsync(await DemoModuleIdAsync())).Succeeded.ShouldBeTrue();

        await CreateSeeder().SeedAsync([new SecondRelease()]);

        (await FunctionAsync("demo.thing.export")).IsEnabled.ShouldBeFalse();
        (await FunctionAsync("demo.thing.view")).IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Enabling_the_module_again_brings_the_new_code_with_it()
    {
        await CreateSeeder().SeedAsync([new FirstRelease()]);
        var moduleId = await DemoModuleIdAsync();
        (await Service().DisableModuleAsync(moduleId)).Succeeded.ShouldBeTrue();
        await CreateSeeder().SeedAsync([new SecondRelease()]);

        (await Service().EnableModuleAsync(moduleId)).Succeeded.ShouldBeTrue();

        (await FunctionAsync("demo.thing.export")).IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_code_declared_under_an_enabled_module_is_seeded_enabled()
    {
        await CreateSeeder().SeedAsync([new FirstRelease()]);

        await CreateSeeder().SeedAsync([new SecondRelease()]);

        (await FunctionAsync("demo.thing.export")).IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_function_created_under_a_disabled_module_starts_disabled()
    {
        await CreateSeeder().SeedAsync([new FirstRelease()]);
        var moduleId = await DemoModuleIdAsync();
        (await Service().DisableModuleAsync(moduleId)).Succeeded.ShouldBeTrue();

        var result = await Service().CreateModuleFunctionAsync(new CreateModuleFunctionRequest
        {
            ModuleId = moduleId, Code = "demo.thing.archive", Name = "Archive",
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        (await FunctionAsync("demo.thing.archive")).IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task A_function_created_under_an_enabled_module_starts_enabled()
    {
        await CreateSeeder().SeedAsync([new FirstRelease()]);

        var result = await Service().CreateModuleFunctionAsync(new CreateModuleFunctionRequest
        {
            ModuleId = await DemoModuleIdAsync(), Code = "demo.thing.archive", Name = "Archive",
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        (await FunctionAsync("demo.thing.archive")).IsEnabled.ShouldBeTrue();
    }
}
