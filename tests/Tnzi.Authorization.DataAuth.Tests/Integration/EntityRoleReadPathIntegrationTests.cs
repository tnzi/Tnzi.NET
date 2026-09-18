using System.Text.Json;

namespace Tnzi.Authorization.DataAuth.Tests.Integration;

/// <summary>
/// <c>EntityRole</c> 的<b>管理读路径</b>：按用户 / 按 Id / 按角色三个查询，经真实 SQLite 仓储走一遍，
/// 再把结果按 MVC 的默认方式（<see cref="JsonSerializer"/>，不设 ReferenceHandler）序列化一次。
/// </summary>
/// <remarks>
/// <para>
/// 三个查询都是<b>带跟踪</b>的仓储查询并 <c>Include(er =&gt; er.EntityInfo)</c>；同一批 <c>EntityRole</c> 与它们的
/// <c>EntityInfo</c> 进同一个变更跟踪器后，EF 的关系修复会同时填上 <c>role.EntityInfo</c> 与
/// <c>entityInfo.EntityRoles</c>。端点直接返回实体，于是 <c>EntityRole → EntityInfo → EntityRoles[0] → EntityInfo …</c>
/// 成环，整页 500 —— 与父模块 <c>FunctionModule.Parent ↔ Children</c> 同形。单元测试里 mock 掉仓储看不见这件事：
/// 修复（fix-up）是变更跟踪器的行为，必须用真的。
/// </para>
/// </remarks>
public class EntityRoleReadPathIntegrationTests : IntegratedTestBase<DataAuthTestDbContext>
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid RoleId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EntityInfoId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid EntityRoleId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly Mock<IUserRoleService> _userRoleService = new();

    public EntityRoleReadPathIntegrationTests()
    {
        _userRoleService.Setup(s => s.GetUserRoleIdsAsync(UserId)).ReturnsAsync([RoleId]);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<EntityInfo>(services);
        AddRepo<EntityRole>(services);
        services.AddScoped(_ => _userRoleService.Object);
        services.AddScoped<IDataAuthService>(sp => new DataAuthService(
            sp.GetRequiredService<IRepository<EntityInfo, Guid>>(),
            sp.GetRequiredService<IRepository<EntityRole, Guid>>(),
            sp,
            sp.GetRequiredService<IUserRoleService>()));
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<DataAuthTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<DataAuthTestDbContext>(), serviceProvider: sp));
    }

    private IDataAuthService Service => ServiceProvider.GetRequiredService<IDataAuthService>();

    /// <summary>一个登记 + 一条挂在它上面的角色规则。</summary>
    private async Task SeedAsync()
    {
        var entityInfos = ServiceProvider.GetRequiredService<IRepository<EntityInfo, Guid>>();
        await entityInfos.InsertAsync(new EntityInfo { Id = EntityInfoId, Name = "Ticket", TypeName = typeof(Ticket).FullName! });

        var entityRoles = ServiceProvider.GetRequiredService<IRepository<EntityRole, Guid>>();
        await entityRoles.InsertAsync(new EntityRole
        {
            Id = EntityRoleId, EntityInfoId = EntityInfoId, RoleId = RoleId, Operation = DataAuthOperation.Query, IsEnabled = true,
        });
    }

    /// <summary>MVC 默认的序列化方式：没有 ReferenceHandler，遇到环就抛 <see cref="JsonException"/>。</summary>
    private static string SerializeLikeMvc<T>(T value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>关系修复确实发生了 —— 否则这几条用例守的不是一件会变的事。</summary>
    private static void AssertFixedUp(EntityRole role)
    {
        role.EntityInfo.ShouldNotBeNull();
        role.EntityInfo.EntityRoles.ShouldContain(role);
    }

    private static void AssertSerializedShape(JsonElement role)
    {
        role.GetProperty("entityInfoId").GetGuid().ShouldBe(EntityInfoId);
        // 反向集合不进 JSON；正向导航可保留，但它下面绝不能再带回集合。
        if (role.TryGetProperty("entityInfo", out var entityInfo))
        {
            entityInfo.TryGetProperty("entityRoles", out _).ShouldBeFalse("EntityInfo.EntityRoles must not be serialized");
        }
    }

    /// <summary>★按用户：管理端「查看用户的数据权限」走的就是这条。</summary>
    [Fact]
    public async Task GetUserEntityRoles_WithOneRow_SerializesWithoutCycle()
    {
        await SeedAsync();

        var result = await Service.GetUserEntityRolesAsync(UserId);

        result.Succeeded.ShouldBeTrue(result.Message);
        var roles = result.Data!.ToList();
        roles.Count.ShouldBe(1);
        AssertFixedUp(roles[0]);

        var json = Should.NotThrow(() => SerializeLikeMvc(result.Data));

        using var document = JsonDocument.Parse(json);
        AssertSerializedShape(document.RootElement[0]);
    }

    /// <summary>★按 Id：单条也成环，环不需要两行。</summary>
    [Fact]
    public async Task GetEntityRoleById_WithOneRow_SerializesWithoutCycle()
    {
        await SeedAsync();

        var result = await Service.GetEntityRoleByIdAsync(EntityRoleId);

        result.Succeeded.ShouldBeTrue(result.Message);
        AssertFixedUp(result.Data!);

        var json = Should.NotThrow(() => SerializeLikeMvc(result.Data));

        using var document = JsonDocument.Parse(json);
        AssertSerializedShape(document.RootElement);
    }

    /// <summary>★按角色：同一批行、同一个跟踪器。</summary>
    [Fact]
    public async Task GetEntityRolesByRole_WithOneRow_SerializesWithoutCycle()
    {
        await SeedAsync();

        var result = await Service.GetEntityRolesByRoleAsync(RoleId);

        result.Succeeded.ShouldBeTrue(result.Message);
        var roles = result.Data!.ToList();
        roles.Count.ShouldBe(1);
        AssertFixedUp(roles[0]);

        var json = Should.NotThrow(() => SerializeLikeMvc(result.Data));

        using var document = JsonDocument.Parse(json);
        AssertSerializedShape(document.RootElement[0]);
    }
}
