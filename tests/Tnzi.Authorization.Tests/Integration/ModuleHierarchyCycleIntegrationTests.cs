using System.Text.Json;
using Mapster;
using MapsterMapper;
using Tnzi.Mapster;
using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 模块父子关系不得成环。真实 SQLite 仓储。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 此前改父模块只拦「自己做自己的父」：把根模块 A 挂到它的子模块 B 下面照样 200，于是
/// 级联启停（无去重的广度优先遍历）在 A→B→A 上永不结束，事务与连接一直占着；
/// 模块树里 A、B 互为子节点、谁都不是根，整组连同功能一起从树里消失；
/// 模块列表直接返回实体，关系修复把环填进 Children，序列化溢出深度整页 500，界面上再也修不回来。
/// </para>
/// <para>
/// 写入口拒绝成环；读路径与级联对库里已经存在的环（写入口加守卫之前留下的）也要能终止、不丢模块。
/// </para>
/// </remarks>
public class ModuleHierarchyCycleIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    /// <summary>级联遍历若不终止，测试要失败而不是挂住整个测试进程。</summary>
    private static readonly TimeSpan CascadeDeadline = TimeSpan.FromSeconds(15);

    private readonly AuthOptions _authOptions = new();

    public ModuleHierarchyCycleIntegrationTests()
    {
        // 改模块走 MapTo，静态映射器在测试宿主里没人初始化过。
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

    private FunctionAuthorizationService Service => ServiceProvider.GetRequiredService<FunctionAuthorizationService>();

    /// <summary>A 为根，B 挂在 A 下，C 挂在 B 下；每个模块一个功能。</summary>
    private async Task SeedChainAsync()
    {
        DbContext.FunctionModules.AddRange(
            Module(A, "a", parentId: null),
            Module(B, "a.b", parentId: A),
            Module(C, "a.b.c", parentId: B));
        DbContext.ModuleFunctions.AddRange(Function(A, "a.view"), Function(B, "a.b.view"), Function(C, "a.b.c.view"));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>直接写库造出 A↔B 的环（写入口加守卫之前留下的数据形状），C 挂在 A 下。</summary>
    private async Task SeedExistingCycleAsync()
    {
        // 分两次保存：一次插入里的互指外键 EF 排不出插入顺序。
        DbContext.FunctionModules.AddRange(
            Module(A, "a", parentId: null),
            Module(B, "b", parentId: A),
            Module(C, "a.c", parentId: A));
        DbContext.ModuleFunctions.AddRange(Function(A, "a.view"), Function(B, "b.view"), Function(C, "a.c.view"));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        (await DbContext.FunctionModules.SingleAsync(m => m.Id == A)).ParentId = B;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private static FunctionModule Module(Guid id, string code, Guid? parentId) => new()
    {
        Id = id, Code = code, Name = code, ParentId = parentId, IsEnabled = true, CreationTime = DateTime.UtcNow,
    };

    private static ModuleFunction Function(Guid moduleId, string code) => new()
    {
        Id = Guid.NewGuid(), ModuleId = moduleId, Code = code, Name = code, IsEnabled = true, CreationTime = DateTime.UtcNow,
    };

    private static UpdateFunctionModuleRequest MoveRequest(string code, Guid? parentId)
        => new() { Code = code, Name = code, ParentId = parentId };

    private async Task<FunctionModule> ReadAsync(Guid id)
        => await DbContext.FunctionModules.AsNoTracking().SingleAsync(m => m.Id == id);

    private static string SerializeLikeMvc<T>(T value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public async Task A_module_cannot_be_moved_under_its_own_child()
    {
        await SeedChainAsync();

        var result = await Service.UpdateModuleAsync(A, MoveRequest("a", B));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await ReadAsync(A)).ParentId.ShouldBeNull();
    }

    [Fact]
    public async Task A_module_cannot_be_moved_under_a_deeper_descendant()
    {
        await SeedChainAsync();

        var result = await Service.UpdateModuleAsync(A, MoveRequest("a", C));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await ReadAsync(A)).ParentId.ShouldBeNull();
    }

    /// <summary>反向对照：挂到一个不在自己子树里的模块下照常放行，守卫不能把正常的移动一起拦掉。</summary>
    [Fact]
    public async Task A_module_can_still_be_moved_under_a_module_outside_its_subtree()
    {
        await SeedChainAsync();

        var result = await Service.UpdateModuleAsync(C, MoveRequest("a.b.c", A));

        result.Succeeded.ShouldBeTrue(result.Message);
        (await ReadAsync(C)).ParentId.ShouldBe(A);
    }

    /// <summary>挂到一条本身已经成环的祖先链下面同样拒绝：那个位置在树上不可达，模块会跟着整组消失。</summary>
    [Fact]
    public async Task A_module_cannot_be_moved_under_an_existing_cycle()
    {
        await SeedExistingCycleAsync();
        DbContext.FunctionModules.Add(Module(Guid.NewGuid(), "d", parentId: null));
        await DbContext.SaveChangesAsync();
        var d = (await DbContext.FunctionModules.AsNoTracking().SingleAsync(m => m.Code == "d")).Id;
        DbContext.ChangeTracker.Clear();

        var result = await Service.UpdateModuleAsync(d, MoveRequest("d", B));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Disabling_a_module_on_cyclic_data_terminates_and_visits_each_module_once()
    {
        await SeedExistingCycleAsync();

        // 放到线程池上跑：内存 SQLite 的异步调用同步完成，不终止的遍历会把调用线程一直占住，截止时间永远轮不到。
        var service = Service;
        var disable = Task.Run(() => service.DisableModuleAsync(A));
        var finished = await Task.WhenAny(disable, Task.Delay(CascadeDeadline));

        finished.ShouldBeSameAs(disable, "the cascade must terminate on a parent cycle");
        var result = await disable;
        result.Succeeded.ShouldBeTrue(result.Message);
        result.Message!.ShouldContain("affected 3 modules");
        (await DbContext.FunctionModules.AsNoTracking().CountAsync(m => m.IsEnabled)).ShouldBe(0);
        (await DbContext.ModuleFunctions.AsNoTracking().CountAsync(f => f.IsEnabled)).ShouldBe(0);
    }

    /// <summary>环上的模块不能从树里消失（谁都不是根就谁都不输出），也不能让序列化成环。</summary>
    [Fact]
    public async Task The_module_tree_keeps_modules_on_a_cycle_and_serializes()
    {
        await SeedExistingCycleAsync();

        var result = await Service.GetModuleTreeAsync();

        result.Succeeded.ShouldBeTrue();
        var json = Should.NotThrow(() => SerializeLikeMvc(result.Data));
        json.ShouldContain("\"code\":\"a\"");
        json.ShouldContain("\"code\":\"b\"");
        json.ShouldContain("\"code\":\"a.c\"");
    }

    /// <summary>模块列表是管理页修复数据的入口：库里有环时它必须还能返回，否则界面上无从修起。</summary>
    [Fact]
    public async Task The_module_list_serializes_on_cyclic_data()
    {
        await SeedExistingCycleAsync();

        var result = await Service.GetModulesAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.Count().ShouldBe(3);
        Should.NotThrow(() => SerializeLikeMvc(result.Data));
    }
}
