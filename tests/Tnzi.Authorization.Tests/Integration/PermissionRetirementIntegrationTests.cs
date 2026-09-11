using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 权限码「不再被任何 provider 声明」时的处置：默认保留数据行与授权，只标记退役。
/// </summary>
/// <remarks>
/// <para>
/// 一个码停止被声明，最常见的原因不是「产品删掉了这个权限」，而是<b>这个部署没有加载那个模块</b>。
/// 权限码跟着 provider 类走，所以把模块拆成子模块之后，升级了框架却没有补上子模块
/// <c>[DependsOn]</c> 的宿主，下一次启动就不再声明那些码。
/// </para>
/// <para>
/// 旧行为是删行 + 删 <c>RoleFunction</c> 授权，而且**不可逆**：软删过滤器让 seeder 看不见
/// 墓碑行，重新声明时会插入一条新 id 的行，旧授权永远接不回来。
/// 这组测试锁住新的默认语义，并保留 <c>Delete</c> 模式的历史行为。
/// </para>
/// </remarks>
public class PermissionRetirementIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private static readonly Guid RoleId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid GrantId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private readonly AuthOptions _authOptions = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<FunctionModule>(services);
        AddRepo<ModuleFunction>(services);
        AddRepo<RoleFunction>(services);
        AddRepo<UserFunction>(services);
        services.AddScoped(_ => MsOptions.Create(_authOptions));
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<AuthorizationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<AuthorizationTestDbContext>(), serviceProvider: sp));
    }

    /// <summary>只声明 <c>demo.thing.view</c> 的 provider。</summary>
    private sealed class DeclaringProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("demo", "Demo");
            context.AddPermission("demo.thing.view", "View", parentName: "demo");
        }
    }

    /// <summary>模块卸载后的状态：组还在（别的码撑着），但那个码没人声明了。</summary>
    private sealed class SilentProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("demo", "Demo");
            context.AddPermission("demo.other.view", "Other", parentName: "demo");
        }
    }

    private PermissionDbSeeder CreateSeeder() => new(
        ServiceProvider.GetRequiredService<IRepository<FunctionModule, Guid>>(),
        ServiceProvider.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<PermissionDbSeeder>.Instance,
        MsOptions.Create(_authOptions),
        ServiceProvider.GetRequiredService<IRepository<RoleFunction, Guid>>(),
        ServiceProvider.GetRequiredService<IRepository<UserFunction, Guid>>());

    /// <summary>先播种一次，再给那个码挂一条角色授权。</summary>
    private async Task<(Guid FunctionId, Guid GrantId)> SeedAndGrantAsync()
    {
        await CreateSeeder().SeedAsync([new DeclaringProvider()]);

        var functions = ServiceProvider.GetRequiredService<IRepository<ModuleFunction, Guid>>();
        var function = await functions.FirstOrDefaultAsync(f => f.Code == "demo.thing.view");
        Assert.NotNull(function);

        var grants = ServiceProvider.GetRequiredService<IRepository<RoleFunction, Guid>>();
        await grants.InsertAsync(new RoleFunction
        {
            Id = GrantId,
            RoleId = RoleId,
            FunctionId = function!.Id,
            IsEnabled = true,
        });

        return (function.Id, GrantId);
    }

    /// <summary>★默认：数据行与授权都留着，只是被标记退役。</summary>
    [Fact]
    public async Task Default_KeepsTheRowAndTheGrant_AndOnlyMarksItRetired()
    {
        var (functionId, grantId) = await SeedAndGrantAsync();

        await CreateSeeder().SeedAsync([new SilentProvider()]);

        var function = await ServiceProvider.GetRequiredService<IRepository<ModuleFunction, Guid>>().GetAsync(functionId);
        Assert.NotNull(function);
        Assert.True(function!.IsRetired);

        var grant = await ServiceProvider.GetRequiredService<IRepository<RoleFunction, Guid>>().GetAsync(grantId);
        Assert.NotNull(grant);
    }

    /// <summary>★重新声明该码：退役标记清除，授权原样还在（这正是删除做不到的那一步）。</summary>
    [Fact]
    public async Task RedeclaringTheCode_UnretiresTheSameRow_AndTheGrantSurvives()
    {
        var (functionId, grantId) = await SeedAndGrantAsync();
        await CreateSeeder().SeedAsync([new SilentProvider()]);

        await CreateSeeder().SeedAsync([new DeclaringProvider()]);

        var functions = ServiceProvider.GetRequiredService<IRepository<ModuleFunction, Guid>>();
        var function = await functions.GetAsync(functionId);
        Assert.NotNull(function);
        Assert.False(function!.IsRetired);

        // 同一行，不是新插入的：否则旧授权指向的是一个已经没人用的 id。
        var all = await functions.ToListAsync(f => f.Code == "demo.thing.view");
        Assert.Single(all);
        Assert.Equal(functionId, all[0].Id);

        var grant = await ServiceProvider.GetRequiredService<IRepository<RoleFunction, Guid>>().GetAsync(grantId);
        Assert.NotNull(grant);
        Assert.Equal(functionId, grant!.FunctionId);
    }

    /// <summary>Delete 模式保留历史行为：行与授权一起消失。</summary>
    [Fact]
    public async Task DeleteMode_StillRemovesTheRowAndItsGrants()
    {
        await SeedAndGrantAsync();
        _authOptions.PermissionRetirement = Tnzi.Authorization.Options.PermissionRetirementMode.Delete;

        await CreateSeeder().SeedAsync([new SilentProvider()]);

        // 用查询而不是按 id 取：软删走 ExecuteUpdate，不会更新变更跟踪器里那份实例，
        // 按 id 取会命中跟踪缓存并返回一条其实已被过滤掉的行。
        var functions = await ServiceProvider.GetRequiredService<IRepository<ModuleFunction, Guid>>()
            .ToListAsync(f => f.Code == "demo.thing.view");
        Assert.Empty(functions);

        var grants = await ServiceProvider.GetRequiredService<IRepository<RoleFunction, Guid>>()
            .ToListAsync(rf => rf.RoleId == RoleId);
        Assert.Empty(grants);
    }

    /// <summary>
    /// Delete 模式也要带走<b>用户直授</b>（allow 与 deny 两种行）。
    /// </summary>
    /// <remarks>
    /// 此前只删 <c>RoleFunction</c>。<c>ModuleFunction</c> 是软删，级联外键永远不会触发，
    /// 于是 allow / deny 行继续指向一块墓碑；该码重新声明时插入的是一条新 id 的行（软删过滤器
    /// 让 seeder 看不见墓碑），旧直授永久不可见也无法清理。手工删除路径在有直授时会<b>拒绝</b>删除，
    /// 两处对同一件事得出相反结论。
    /// </remarks>
    [Fact]
    public async Task DeleteMode_AlsoRemovesUserDirectGrants_AllowAndDeny()
    {
        var (functionId, _) = await SeedAndGrantAsync();
        var userFunctions = ServiceProvider.GetRequiredService<IRepository<UserFunction, Guid>>();
        await userFunctions.InsertAsync(new UserFunction { UserId = Guid.NewGuid(), FunctionId = functionId, IsGranted = true });
        await userFunctions.InsertAsync(new UserFunction { UserId = Guid.NewGuid(), FunctionId = functionId, IsGranted = false });
        _authOptions.PermissionRetirement = Tnzi.Authorization.Options.PermissionRetirementMode.Delete;

        await CreateSeeder().SeedAsync([new SilentProvider()]);

        var remaining = await userFunctions.ToListAsync(uf => uf.FunctionId == functionId);
        Assert.Empty(remaining);
    }

    /// <summary>默认（Disable）模式下用户直授必须原样保留 —— 那正是不删的理由。</summary>
    [Fact]
    public async Task DefaultMode_KeepsUserDirectGrants()
    {
        var (functionId, _) = await SeedAndGrantAsync();
        var userFunctions = ServiceProvider.GetRequiredService<IRepository<UserFunction, Guid>>();
        await userFunctions.InsertAsync(new UserFunction { UserId = Guid.NewGuid(), FunctionId = functionId, IsGranted = true });

        await CreateSeeder().SeedAsync([new SilentProvider()]);

        Assert.Single(await userFunctions.ToListAsync(uf => uf.FunctionId == functionId));
    }

    /// <summary>Off 模式什么都不做 —— 多个部署共用一个库、各加载不同模块子集时用。</summary>
    [Fact]
    public async Task OffMode_LeavesUndeclaredRowsAlone()
    {
        var (functionId, _) = await SeedAndGrantAsync();
        _authOptions.PermissionRetirement = Tnzi.Authorization.Options.PermissionRetirementMode.Off;

        await CreateSeeder().SeedAsync([new SilentProvider()]);

        var function = await ServiceProvider.GetRequiredService<IRepository<ModuleFunction, Guid>>().GetAsync(functionId);
        Assert.NotNull(function);
        Assert.False(function!.IsRetired);
    }

    /// <summary>
    /// 退役期间它必须不再授权：留着行的前提是它不能继续生效。
    /// </summary>
    [Fact]
    public async Task RetiredCode_DoesNotResolveAsAPermission()
    {
        await SeedAndGrantAsync();
        await CreateSeeder().SeedAsync([new SilentProvider()]);

        var codes = await ServiceProvider.GetRequiredService<IRepository<ModuleFunction, Guid>>()
            .ToListAsync(f => f.IsEnabled && !f.IsRetired);

        Assert.DoesNotContain(codes, f => f.Code == "demo.thing.view");
    }
}
