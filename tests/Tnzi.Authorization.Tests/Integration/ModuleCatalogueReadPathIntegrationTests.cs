using System.Text.Json;
using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 模块目录的<b>管理读路径</b>：模块树 / 模块列表 / 功能列表三个查询，经真实 SQLite 仓储走一遍，
/// 再把结果按 MVC 的默认方式（<see cref="JsonSerializer"/>，不设 ReferenceHandler）序列化一次。
/// </summary>
/// <remarks>
/// <para>
/// 这三个查询都是<b>带跟踪</b>的仓储查询（<c>IRepository&lt;T&gt;</c> 实现 <c>IQueryable</c> 时走 <c>DbSet.AsQueryable()</c>），
/// 父子模块进同一个变更跟踪器之后 EF 的关系修复会同时填上 <c>child.Parent</c> 与 <c>parent.Children</c>；
/// 而端点直接返回实体，所以任何一条没有 <c>[JsonIgnore]</c> 的反向导航都会让序列化成环、整页 500。
/// 单元测试里 mock 掉仓储看不见这件事 —— 修复（fix-up）是变更跟踪器的行为，必须用真的。
/// </para>
/// </remarks>
public class ModuleCatalogueReadPathIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private static readonly Guid ParentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ChildId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly AuthOptions _authOptions = new();

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

    /// <summary>一个父模块 + 一个带 ParentId 的子模块，子模块下挂一个功能。</summary>
    private async Task SeedParentAndChildAsync()
    {
        var modules = ServiceProvider.GetRequiredService<IRepository<FunctionModule, Guid>>();
        await modules.InsertAsync(new FunctionModule { Id = ParentId, Name = "Parent", Code = "parent", Order = 1 });
        await modules.InsertAsync(new FunctionModule { Id = ChildId, Name = "Child", Code = "parent.child", Order = 2, ParentId = ParentId });

        var functions = ServiceProvider.GetRequiredService<IRepository<ModuleFunction, Guid>>();
        await functions.InsertAsync(new ModuleFunction { Id = Guid.NewGuid(), ModuleId = ChildId, Name = "View", Code = "parent.child.view", Order = 1 });
    }

    /// <summary>MVC 默认的序列化方式：没有 ReferenceHandler，遇到环就抛 <see cref="JsonException"/>。</summary>
    private static string SerializeLikeMvc<T>(T value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>★模块树：子节点挂在 parent.Children 下，而 JSON 里不出现 "parent" 反向导航。</summary>
    [Fact]
    public async Task GetModuleTree_WithParentChildModules_SerializesWithoutCycle()
    {
        await SeedParentAndChildAsync();

        var result = await Service.GetModuleTreeAsync();

        result.Succeeded.ShouldBeTrue();
        var roots = result.Data!.ToList();
        roots.Count.ShouldBe(1);
        roots[0].Id.ShouldBe(ParentId);
        roots[0].Children.ShouldContain(m => m.Id == ChildId);
        // 关系修复确实发生了 —— 否则这条用例守的不是一件会变的事。
        roots[0].Children.Single().Parent.ShouldNotBeNull();

        var json = Should.NotThrow(() => SerializeLikeMvc(result.Data));

        using var document = JsonDocument.Parse(json);
        var child = document.RootElement[0].GetProperty("children")[0];
        child.TryGetProperty("parent", out _).ShouldBeFalse("the Parent back-reference must not be serialized");
        child.GetProperty("functions").GetArrayLength().ShouldBe(1);
    }

    /// <summary>★模块列表：同一批行、同一个跟踪器，父子互指同样成环。</summary>
    [Fact]
    public async Task GetModules_WithParentChildModules_SerializesWithoutCycle()
    {
        await SeedParentAndChildAsync();

        var result = await Service.GetModulesAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.Count().ShouldBe(2);

        var json = Should.NotThrow(() => SerializeLikeMvc(result.Data));
        json.ShouldContain("\"parentId\":\"11111111-1111-1111-1111-111111111111\"");
    }

    /// <summary>
    /// ★功能列表是管理面唯一的目录读路径，停用行必须留在里面（且 IsEnabled=false）：
    /// 它从列表里消失，前端的「启用」按钮就没有宿主，停用在 UI 上变成单向操作。
    /// </summary>
    [Fact]
    public async Task GetModuleFunctions_IncludesDisabledRow_WithIsEnabledFalse()
    {
        await SeedParentAndChildAsync();
        var functions = ServiceProvider.GetRequiredService<IRepository<ModuleFunction, Guid>>();
        await functions.InsertAsync(new ModuleFunction
        {
            Id = Guid.NewGuid(), ModuleId = ChildId, Name = "Delete", Code = "parent.child.delete", Order = 2, IsEnabled = false,
        });

        var result = await Service.GetModuleFunctionsAsync(ChildId);

        result.Succeeded.ShouldBeTrue();
        var rows = result.Data!.ToList();
        rows.Count.ShouldBe(2);
        rows.Single(f => f.Code == "parent.child.delete").IsEnabled.ShouldBeFalse();
        rows.Single(f => f.Code == "parent.child.view").IsEnabled.ShouldBeTrue();
    }

    /// <summary>退役行同理（钉住既有 remark：管理页必须看得见休眠行，否则「授权还在却不生效」无处可查）。</summary>
    [Fact]
    public async Task GetModuleFunctions_IncludesRetiredRow_WithIsRetiredTrue()
    {
        await SeedParentAndChildAsync();
        var functions = ServiceProvider.GetRequiredService<IRepository<ModuleFunction, Guid>>();
        await functions.InsertAsync(new ModuleFunction
        {
            Id = Guid.NewGuid(), ModuleId = ChildId, Name = "Export", Code = "parent.child.export", Order = 3, IsRetired = true,
        });

        var result = await Service.GetModuleFunctionsAsync(ChildId);

        result.Data!.Single(f => f.Code == "parent.child.export").IsRetired.ShouldBeTrue();
    }
}
