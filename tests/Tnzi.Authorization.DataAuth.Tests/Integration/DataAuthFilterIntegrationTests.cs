namespace Tnzi.Authorization.DataAuth.Tests.Integration;

/// <summary>
/// <see cref="DataAuthService"/> 的过滤器路径，经真实 SQLite 仓储 + 真实服务走一遍（此前这条路零测试：
/// 服务用例全是 Mock 仓储、扩展方法用例全是 Mock 服务，没有一条真的执行过 <c>GetDataFilterAsync</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 三条不变量：
/// </para>
/// <list type="number">
///   <item>过滤器 JSON 与列表 API 是<b>同一种方言</b>（大小写不敏感 + 字符串枚举）；</item>
///   <item>查询侧任何一条规则读不出来（非法 JSON / 写了却没规则）都是 <b>deny-on-fail</b>，追加 <c>e =&gt; false</c> 而不是跳过 ——
///     跳过会让「有限可见」退化成「完全无过滤」，方向是放开且零症状；</item>
///   <item>保存侧对同样的输入必须 400，而不是 200 落库然后在查询时静默变成零行。</item>
/// </list>
/// <para>被过滤的实体 <see cref="Ticket"/> 住在测试程序集里，与消费方实体的处境相同。</para>
/// </remarks>
public class DataAuthFilterIntegrationTests : IntegratedTestBase<DataAuthTestDbContext>
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid RoleId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OwnedTicketId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ForeignTicketId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly Mock<IUserRoleService> _userRoleService = new();

    public DataAuthFilterIntegrationTests()
    {
        // 写路径的 request.MapTo<EntityRole>() 要 Mapster；默认配置即可。
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        _userRoleService
            .Setup(s => s.GetUserRoleIdsAsync(UserId))
            .ReturnsAsync([RoleId]);
        _userRoleService
            .Setup(s => s.GetUserRoleIdsAsync(It.Is<Guid>(id => id != UserId)))
            .ReturnsAsync([]);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<EntityInfo>(services);
        AddRepo<EntityRole>(services);
        AddRepo<Ticket>(services);
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

    /// <summary>登记 Ticket、给角色一条 Query 规则（Filter 原样写入，绕过保存期校验）、插两张工单：一张属于用户，一张不属于。</summary>
    private async Task<EntityInfo> SeedAsync(string? filter)
    {
        var entityInfos = ServiceProvider.GetRequiredService<IRepository<EntityInfo, Guid>>();
        var entityInfo = new EntityInfo { Name = "Ticket", TypeName = typeof(Ticket).FullName!, IsDataAuthEnabled = true };
        await entityInfos.InsertAsync(entityInfo);

        var entityRoles = ServiceProvider.GetRequiredService<IRepository<EntityRole, Guid>>();
        await entityRoles.InsertAsync(new EntityRole
        {
            EntityInfoId = entityInfo.Id, RoleId = RoleId, Operation = DataAuthOperation.Query, Filter = filter, IsEnabled = true,
        });

        var tickets = ServiceProvider.GetRequiredService<IRepository<Ticket, Guid>>();
        await tickets.InsertAsync(new Ticket { Id = OwnedTicketId, OwnerId = UserId, Title = "mine" });
        await tickets.InsertAsync(new Ticket { Id = ForeignTicketId, OwnerId = Guid.NewGuid(), Title = "theirs" });
        return entityInfo;
    }

    private async Task<List<Ticket>> VisibleTicketsAsync()
    {
        var filter = await Service.GetDataFilterAsync<Ticket>(UserId, DataAuthOperation.Query);
        var tickets = ServiceProvider.GetRequiredService<IRepository<Ticket, Guid>>();
        var query = filter == null ? tickets.AsQueryable() : tickets.AsQueryable().Where(filter);
        return await query.ToListAsync();
    }

    private static string OwnerFilter(Guid ownerId)
        => $$$"""{"logic":"And","rules":[{"field":"OwnerId","operator":"Equal","value":"{{{ownerId}}}"}]}""";

    /// <summary>★列表 API 方言（camelCase + 字符串枚举）在这里必须是同一个意思：只看得见自己的那一行。</summary>
    [Fact]
    public async Task GetDataFilter_CamelCaseStringEnumFilter_IsHonoured()
    {
        await SeedAsync(OwnerFilter(UserId));

        var visible = await VisibleTicketsAsync();

        visible.Select(t => t.Id).ShouldBe([OwnedTicketId]);
    }

    /// <summary>★非法 JSON 是 deny-on-fail：零行，不是全表。</summary>
    [Fact]
    public async Task GetDataFilter_MalformedFilterJson_YieldsDenyAll()
    {
        await SeedAsync("{not json");

        var visible = await VisibleTicketsAsync();

        visible.ShouldBeEmpty();
    }

    /// <summary>★写了却没规则（键名拼错）同样是 deny-on-fail：一个写下的过滤器只可能是要限制什么。</summary>
    [Fact]
    public async Task GetDataFilter_FilterWithoutRules_YieldsDenyAll()
    {
        await SeedAsync("""{"rule":[{"field":"OwnerId","operator":"Equal","value":"x"}]}""");

        var visible = await VisibleTicketsAsync();

        visible.ShouldBeEmpty();
    }

    /// <summary>留空的 Filter 才是「这个角色对这张表不设限」：既有语义，钉住以免与上一条混淆。</summary>
    [Fact]
    public async Task GetDataFilter_BlankFilter_MeansNoRestriction()
    {
        await SeedAsync(filter: null);

        (await Service.GetDataFilterAsync<Ticket>(UserId, DataAuthOperation.Query)).ShouldBeNull();
        (await VisibleTicketsAsync()).Count.ShouldBe(2);
    }

    private static readonly Guid SecondRoleId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    /// <summary>给用户加第二个角色，并在同一登记上给它一条 Query 规则。</summary>
    private async Task SeedSecondRoleAsync(EntityInfo entityInfo, string? filter)
    {
        _userRoleService
            .Setup(s => s.GetUserRoleIdsAsync(UserId))
            .ReturnsAsync([RoleId, SecondRoleId]);
        var entityRoles = ServiceProvider.GetRequiredService<IRepository<EntityRole, Guid>>();
        await entityRoles.InsertAsync(new EntityRole
        {
            EntityInfoId = entityInfo.Id, RoleId = SecondRoleId, Operation = DataAuthOperation.Query, Filter = filter, IsEnabled = true,
        });
    }

    /// <summary>
    /// ★多角色下「留空 = 不设限」必须按角色读：角色按 OR 合并，一个不设限的角色就是 OR 里的 <c>true</c>，
    /// 另一个角色再窄也收不回去。此前留空被当成「不贡献任何行」（continue），于是持有不设限角色的用户
    /// 反而被第二个角色的过滤器限住 —— 与文档承诺的语义相反。
    /// </summary>
    [Fact]
    public async Task GetDataFilter_BlankRolePlusRestrictedRole_MeansNoRestriction()
    {
        var entityInfo = await SeedAsync(filter: null);
        await SeedSecondRoleAsync(entityInfo, OwnerFilter(Guid.NewGuid()));

        (await Service.GetDataFilterAsync<Ticket>(UserId, DataAuthOperation.Query)).ShouldBeNull();
        (await VisibleTicketsAsync()).Count.ShouldBe(2);
    }

    /// <summary>
    /// ★留空 + 另一角色的过滤器损坏：deny-on-fail 让坏角色<b>不贡献任何行</b>，它不能把别的角色给出的行也收走。
    /// OR(true, false) 是 true；此前这一组合是零行。
    /// </summary>
    [Fact]
    public async Task GetDataFilter_BlankRolePlusMalformedRole_MeansNoRestriction()
    {
        var entityInfo = await SeedAsync(filter: null);
        await SeedSecondRoleAsync(entityInfo, "{not json");

        (await Service.GetDataFilterAsync<Ticket>(UserId, DataAuthOperation.Query)).ShouldBeNull();
        (await VisibleTicketsAsync()).Count.ShouldBe(2);
    }

    /// <summary>对照：两个角色都有过滤器时仍是 OR —— 各自的行都看得见，别的行看不见。</summary>
    [Fact]
    public async Task GetDataFilter_TwoRestrictedRoles_AreOrCombined()
    {
        var entityInfo = await SeedAsync(OwnerFilter(UserId));
        var thirdOwner = Guid.NewGuid();
        await SeedSecondRoleAsync(entityInfo, OwnerFilter(thirdOwner));
        var tickets = ServiceProvider.GetRequiredService<IRepository<Ticket, Guid>>();
        var thirdTicketId = Guid.NewGuid();
        await tickets.InsertAsync(new Ticket { Id = thirdTicketId, OwnerId = thirdOwner, Title = "second role" });

        var visible = await VisibleTicketsAsync();

        visible.Select(t => t.Id).OrderBy(id => id).ShouldBe(new[] { OwnedTicketId, thirdTicketId }.OrderBy(id => id));
    }

    /// <summary>坏 JSON 在保存侧就要被拒绝，而不是 200 落库、查询时静默零行。</summary>
    [Fact]
    public async Task CreateEntityRole_MalformedFilterJson_Returns400()
    {
        var entityInfo = await SeedAsync(filter: null);

        var result = await Service.CreateEntityRoleAsync(new Dtos.CreateEntityRoleRequest
        {
            EntityInfoId = entityInfo.Id, RoleId = Guid.NewGuid(), Operation = DataAuthOperation.Update, Filter = "{not json",
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldNotBeNull();
        result.Message!.ShouldContain("malformed");
    }

    /// <summary>写了却没规则同样 400，文案告诉管理员「不设限就把字段留空」。</summary>
    [Fact]
    public async Task CreateEntityRole_FilterWithNoRules_Returns400()
    {
        var entityInfo = await SeedAsync(filter: null);

        var result = await Service.CreateEntityRoleAsync(new Dtos.CreateEntityRoleRequest
        {
            EntityInfoId = entityInfo.Id, RoleId = Guid.NewGuid(), Operation = DataAuthOperation.Update,
            Filter = """{"rule":[{"field":"OwnerId"}]}""",
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("no rules");
    }

    /// <summary>超过列宽的 JSON 在保存侧 400：宽松 MySQL 会把它静默截成半截，那半截在查询时是零行。</summary>
    [Fact]
    public async Task CreateEntityRole_OverlongFilter_Returns400()
    {
        var entityInfo = await SeedAsync(filter: null);
        var padding = new string(' ', EntityRole.FilterMaxLength);

        var result = await Service.CreateEntityRoleAsync(new Dtos.CreateEntityRoleRequest
        {
            EntityInfoId = entityInfo.Id, RoleId = Guid.NewGuid(), Operation = DataAuthOperation.Update, Filter = padding + OwnerFilter(UserId),
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("exceeds");
    }

    /// <summary>
    /// ★属性名拼错在保存侧就要 400 —— 靶子必须是<b>别的程序集</b>里的实体：此前类型解析用
    /// <c>Type.GetType(裸 FullName)</c>，只在本模块自己的程序集里找，对任何消费方实体恒为 null 然后软放行，
    /// 保存期承诺的 400 从来没发生过；用本模块自己的实体当靶子修前也绿。
    /// </summary>
    [Fact]
    public async Task CreateEntityRole_BadPropertyName_Returns400_ForEntityInAnotherAssembly()
    {
        var entityInfo = await SeedAsync(filter: null);

        var result = await Service.CreateEntityRoleAsync(new Dtos.CreateEntityRoleRequest
        {
            EntityInfoId = entityInfo.Id, RoleId = Guid.NewGuid(), Operation = DataAuthOperation.Update,
            Filter = """{"logic":"And","rules":[{"field":"OwnerId2","operator":"Equal","value":"x"}]}""",
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("OwnerId2");
    }

    /// <summary>登记一个本部署里不存在的实体类型是配置错，当场 400，而不是留一行永远校验不到的登记。</summary>
    [Fact]
    public async Task CreateEntityInfo_UnresolvableTypeName_Returns400()
    {
        var result = await Service.CreateEntityInfoAsync(new Dtos.CreateEntityInfoRequest
        {
            Name = "Ghost", TypeName = "Consumer.Entities.NoSuchEntity",
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("NoSuchEntity");
    }

    /// <summary>裸 FullName（过滤路径查表用的口径）登记成功。</summary>
    [Fact]
    public async Task CreateEntityInfo_BareFullName_Succeeds()
    {
        var result = await Service.CreateEntityInfoAsync(new Dtos.CreateEntityInfoRequest
        {
            Name = "Ticket", TypeName = typeof(Ticket).FullName!,
        });

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    /// <summary>
    /// AssemblyQualifiedName 不是登记口径：过滤路径按 <c>typeof(T).FullName</c> 等值查表，
    /// 存 AQN 的登记永远查不到 ⇒ 整个实体不过滤。登记时就拒绝，别让它安静地放开。
    /// </summary>
    [Fact]
    public async Task CreateEntityInfo_AssemblyQualifiedName_Returns400()
    {
        var result = await Service.CreateEntityInfoAsync(new Dtos.CreateEntityInfoRequest
        {
            Name = "Ticket", TypeName = typeof(Ticket).AssemblyQualifiedName!,
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>
    /// ★批量创建与单条创建共享同一道保存期校验：同一份坏 filter 单条 400、批量 201 然后五个角色全员零行，
    /// 就是「共享出口的后半段漏了一步」。校验失败时一行都不插。
    /// </summary>
    [Fact]
    public async Task BatchCreateEntityRoles_BadFilter_Returns400_AndInsertsNothing()
    {
        var entityInfo = await SeedAsync(filter: null);
        var roleIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        var result = await Service.BatchCreateEntityRolesAsync(new Dtos.BatchEntityRoleRequest
        {
            EntityInfoId = entityInfo.Id, RoleIds = roleIds, Operation = DataAuthOperation.Update,
            Filter = """{"logic":"And","rules":[{"field":"OwnerId2","operator":"Equal","value":"x"}]}""",
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        var rows = await ServiceProvider.GetRequiredService<IRepository<EntityRole, Guid>>()
            .ToListAsync(er => roleIds.Contains(er.RoleId));
        rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task BatchCreateEntityRoles_MalformedJson_Returns400()
    {
        var entityInfo = await SeedAsync(filter: null);

        var result = await Service.BatchCreateEntityRolesAsync(new Dtos.BatchEntityRoleRequest
        {
            EntityInfoId = entityInfo.Id, RoleIds = [Guid.NewGuid()], Operation = DataAuthOperation.Update, Filter = "{not json",
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("malformed");
    }

    /// <summary>
    /// ★<c>admin/data-auth/check</c> 背后的非泛型判定必须真的按行判：此前除「类型未登记 404」外每条路径都 Ok(true)，
    /// 包括「确实有命中的过滤规则」那条 —— 一个恒真的授权判定 API，长得和一次成功的检查一模一样。
    /// </summary>
    [Fact]
    public async Task CheckDataPermissionByTypeName_RowOutsideRoleFilter_ReturnsFalse()
    {
        await SeedAsync(OwnerFilter(UserId));

        var result = await Service.CheckDataPermissionByTypeNameAsync(
            UserId, typeof(Ticket).FullName!, ForeignTicketId, DataAuthOperation.Query);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckDataPermissionByTypeName_RowInsideRoleFilter_ReturnsTrue()
    {
        await SeedAsync(OwnerFilter(UserId));

        var result = await Service.CheckDataPermissionByTypeNameAsync(
            UserId, typeof(Ticket).FullName!, OwnedTicketId, DataAuthOperation.Query);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBeTrue();
    }

    /// <summary>登记了却解析不到 CLR 类型（模块没加载）⇒ 501「本部署做不了这次判定」，不是 Ok(true)。</summary>
    [Fact]
    public async Task CheckDataPermissionByTypeName_UnresolvableType_Returns501()
    {
        var entityInfos = ServiceProvider.GetRequiredService<IRepository<EntityInfo, Guid>>();
        await entityInfos.InsertAsync(new EntityInfo { Name = "Ghost", TypeName = "Consumer.Entities.NoSuchEntity", IsDataAuthEnabled = true });

        var result = await Service.CheckDataPermissionByTypeNameAsync(
            UserId, "Consumer.Entities.NoSuchEntity", Guid.NewGuid(), DataAuthOperation.Query);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
    }

    /// <summary>登记一个 long 主键的实体，并给用户的角色一条 Query 规则（Filter 原样写入）。</summary>
    private async Task SeedLongTicketAsync()
    {
        var entityInfos = ServiceProvider.GetRequiredService<IRepository<EntityInfo, Guid>>();
        var entityInfo = new EntityInfo { Name = "LongTicket", TypeName = typeof(LongTicket).FullName!, IsDataAuthEnabled = true };
        await entityInfos.InsertAsync(entityInfo);
        var entityRoles = ServiceProvider.GetRequiredService<IRepository<EntityRole, Guid>>();
        await entityRoles.InsertAsync(new EntityRole
        {
            EntityInfoId = entityInfo.Id, RoleId = RoleId, Operation = DataAuthOperation.Query, Filter = OwnerFilter(UserId), IsEnabled = true,
        });
    }

    /// <summary>
    /// ★判定接受的是 <c>Guid entityId</c>，对雪花 long 主键（框架一等选项）的登记它做不了：答 501「这次判定做不了」，
    /// 而不是让 <c>Expression.Equal(long, Guid)</c> 在反射调用里炸成 500。此前是 500。
    /// </summary>
    [Fact]
    public async Task CheckDataPermissionByTypeName_NonGuidKeyedEntity_Returns501()
    {
        await SeedLongTicketAsync();

        var result = await Service.CheckDataPermissionByTypeNameAsync(
            UserId, typeof(LongTicket).FullName!, Guid.NewGuid(), DataAuthOperation.Query);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
        result.Message!.ShouldContain("Guid");
    }

    /// <summary>泛型入口对同一情形拒绝（false + Error 日志）而不是抛：与「没有 Id 属性」「仓储未注册」同一方向。</summary>
    [Fact]
    public async Task CheckDataPermission_NonGuidKeyedEntity_DeniesInsteadOfThrowing()
    {
        await SeedLongTicketAsync();

        var allowed = await Service.CheckDataPermissionAsync<LongTicket>(UserId, Guid.NewGuid(), DataAuthOperation.Query);

        allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckDataPermissionByTypeName_UnregisteredType_Returns404()
    {
        var result = await Service.CheckDataPermissionByTypeNameAsync(
            UserId, typeof(Ticket).FullName!, OwnedTicketId, DataAuthOperation.Query);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    /// <summary>合法过滤器照常 201。</summary>
    [Fact]
    public async Task CreateEntityRole_ValidFilter_Succeeds()
    {
        var entityInfo = await SeedAsync(filter: null);

        var result = await Service.CreateEntityRoleAsync(new Dtos.CreateEntityRoleRequest
        {
            EntityInfoId = entityInfo.Id, RoleId = Guid.NewGuid(), Operation = DataAuthOperation.Update, Filter = OwnerFilter(UserId),
        });

        result.Succeeded.ShouldBeTrue(result.Message);
    }
}
