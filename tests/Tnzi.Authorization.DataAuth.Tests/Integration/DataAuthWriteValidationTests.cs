using Tnzi.Authorization.DataAuth.Dtos;

namespace Tnzi.Authorization.DataAuth.Tests.Integration;

/// <summary>
/// 规则与登记的写入口对请求形状的校验：缺省值不能被当成一次合法的改动。真实 SQLite 仓储。
/// </summary>
/// <remarks>
/// <para>
/// ★ 操作类型是按位匹配的（<c>(rule.Operation &amp; op) != 0</c>）。缺省的 0 与未定义的位永远不命中：
/// 一次只带 filter 的 PUT 把已有规则的操作改写成 0，规则从此不匹配任何操作，该角色若没有别的规则就看到全表，
/// 管理端却仍显示一条「有过滤器」的规则；<c>check</c> 漏传操作时对谁都答 true。
/// </para>
/// <para>
/// ★ 登记的更新是整体替换：此前名称缺省为空串、启用开关缺省为 true，只改显示名的调用把名称清空、
/// 把一个停用的实体悄悄重新启用（方向是收紧：用户突然看不到行）。
/// </para>
/// </remarks>
public class DataAuthWriteValidationTests : IntegratedTestBase<DataAuthTestDbContext>
{
    private static readonly Guid RoleId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public DataAuthWriteValidationTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<EntityInfo, Guid>>(sp =>
            new EFCoreRepository<DataAuthTestDbContext, EntityInfo, Guid>(sp.GetRequiredService<DataAuthTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IRepository<EntityRole, Guid>>(sp =>
            new EFCoreRepository<DataAuthTestDbContext, EntityRole, Guid>(sp.GetRequiredService<DataAuthTestDbContext>(), serviceProvider: sp));
        services.AddScoped(_ => new Mock<IUserRoleService>().Object);
        services.AddScoped<IDataAuthService>(sp => new DataAuthService(
            sp.GetRequiredService<IRepository<EntityInfo, Guid>>(),
            sp.GetRequiredService<IRepository<EntityRole, Guid>>(),
            sp,
            sp.GetRequiredService<IUserRoleService>()));
    }

    private IDataAuthService Service => ServiceProvider.CreateScope().ServiceProvider.GetRequiredService<IDataAuthService>();

    private async Task<EntityInfo> SeedEntityInfoAsync(bool isDataAuthEnabled = true)
    {
        var entityInfo = new EntityInfo
        {
            Id = Guid.NewGuid(), Name = "Ticket", DisplayName = "Tickets", TypeName = typeof(Ticket).FullName!, IsDataAuthEnabled = isDataAuthEnabled,
        };
        DbContext.EntityInfos.Add(entityInfo);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return entityInfo;
    }

    private async Task<EntityRole> SeedRuleAsync(Guid entityInfoId)
    {
        var rule = new EntityRole
        {
            Id = Guid.NewGuid(), EntityInfoId = entityInfoId, RoleId = RoleId, Operation = DataAuthOperation.Query,
            Filter = """{"logic":"And","rules":[{"field":"Title","operator":"Equal","value":"mine"}]}""", IsEnabled = true,
        };
        DbContext.EntityRoles.Add(rule);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return rule;
    }

    // ── 操作类型 ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(9)]
    public async Task Creating_a_rule_with_no_or_an_undefined_operation_is_refused(int operation)
    {
        var entityInfo = await SeedEntityInfoAsync();

        var result = await Service.CreateEntityRoleAsync(new CreateEntityRoleRequest
        {
            EntityInfoId = entityInfo.Id, RoleId = RoleId, Operation = (DataAuthOperation)operation,
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await DbContext.EntityRoles.AsNoTracking().CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(DataAuthOperation.Query)]
    [InlineData(DataAuthOperation.Update | DataAuthOperation.Delete)]
    [InlineData(DataAuthOperation.All)]
    public async Task Creating_a_rule_with_a_defined_combination_is_accepted(DataAuthOperation operation)
    {
        var entityInfo = await SeedEntityInfoAsync();

        var result = await Service.CreateEntityRoleAsync(new CreateEntityRoleRequest
        {
            EntityInfoId = entityInfo.Id, RoleId = RoleId, Operation = operation,
        });

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task An_update_without_an_operation_is_refused_and_leaves_the_rule_alone()
    {
        var entityInfo = await SeedEntityInfoAsync();
        var rule = await SeedRuleAsync(entityInfo.Id);

        var result = await Service.UpdateEntityRoleAsync(rule.Id, new UpdateEntityRoleRequest { Filter = null });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        var stored = await DbContext.EntityRoles.AsNoTracking().SingleAsync(r => r.Id == rule.Id);
        stored.Operation.ShouldBe(DataAuthOperation.Query);
        stored.Filter.ShouldBe(rule.Filter);
    }

    [Fact]
    public async Task A_batch_without_an_operation_is_refused()
    {
        var entityInfo = await SeedEntityInfoAsync();

        var result = await Service.BatchCreateEntityRolesAsync(new BatchEntityRoleRequest
        {
            EntityInfoId = entityInfo.Id, RoleIds = [RoleId, Guid.NewGuid()],
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await DbContext.EntityRoles.AsNoTracking().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_check_without_an_operation_is_refused_rather_than_answered_true()
    {
        var entityInfo = await SeedEntityInfoAsync();

        var result = await Service.CheckDataPermissionByTypeNameAsync(Guid.NewGuid(), entityInfo.TypeName, Guid.NewGuid(), 0);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    // ── 登记的更新（整体替换） ──────────────────────────────────────────────

    [Fact]
    public async Task An_entity_info_update_without_a_name_is_refused()
    {
        var entityInfo = await SeedEntityInfoAsync();

        var result = await Service.UpdateEntityInfoAsync(entityInfo.Id, new UpdateEntityInfoRequest
        {
            Name = "  ", DisplayName = "Renamed", IsDataAuthEnabled = true,
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await DbContext.EntityInfos.AsNoTracking().SingleAsync()).Name.ShouldBe("Ticket");
    }

    [Fact]
    public async Task An_entity_info_update_without_the_enabled_flag_does_not_re_enable_the_entity()
    {
        var entityInfo = await SeedEntityInfoAsync(isDataAuthEnabled: false);

        var result = await Service.UpdateEntityInfoAsync(entityInfo.Id, new UpdateEntityInfoRequest
        {
            Name = "Ticket", DisplayName = "Renamed",
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        var stored = await DbContext.EntityInfos.AsNoTracking().SingleAsync();
        stored.IsDataAuthEnabled.ShouldBeFalse();
        stored.DisplayName.ShouldBe("Tickets");
    }

    [Fact]
    public async Task An_entity_info_update_with_every_field_replaces_the_row()
    {
        var entityInfo = await SeedEntityInfoAsync();

        var result = await Service.UpdateEntityInfoAsync(entityInfo.Id, new UpdateEntityInfoRequest
        {
            Name = "Support Ticket", DisplayName = null, IsDataAuthEnabled = false,
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        var stored = await DbContext.EntityInfos.AsNoTracking().SingleAsync();
        stored.Name.ShouldBe("Support Ticket");
        stored.DisplayName.ShouldBeNull();
        stored.IsDataAuthEnabled.ShouldBeFalse();
    }
}
