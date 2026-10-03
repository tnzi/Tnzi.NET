namespace Tnzi.Authorization.DataAuth.Tests.Integration;

/// <summary>
/// 同一 (实体, 角色, 操作) 在未删除的行里只有一条规则。
/// </summary>
/// <remarks>
/// ★ 过滤器按角色把规则并起来：重复的两条里删掉一条，另一条照样生效 —— 页面上已经收回的权限其实还在。
/// 服务层的判重是 read-then-write，并发的两次创建都会通过，所以最终把关的是唯一索引，服务要把撞上它答成 409。
/// </remarks>
public class EntityRoleUniquenessTests : IntegratedTestBase<DataAuthTestDbContext>
{
    private static readonly Guid RoleId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public EntityRoleUniquenessTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<EntityInfo, Guid>>(sp =>
            new EFCoreRepository<DataAuthTestDbContext, EntityInfo, Guid>(sp.GetRequiredService<DataAuthTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IRepository<EntityRole, Guid>>(sp =>
            new RacingEntityRoleRepository(sp.GetRequiredService<DataAuthTestDbContext>(), sp));
        services.AddScoped(_ => new Mock<IUserRoleService>().Object);
        services.AddScoped<IDataAuthService>(sp => new DataAuthService(
            sp.GetRequiredService<IRepository<EntityInfo, Guid>>(),
            sp.GetRequiredService<IRepository<EntityRole, Guid>>(),
            sp,
            sp.GetRequiredService<IUserRoleService>()));
    }

    private IDataAuthService Service => ServiceProvider.GetRequiredService<IDataAuthService>();

    private RacingEntityRoleRepository Repository => (RacingEntityRoleRepository)ServiceProvider.GetRequiredService<IRepository<EntityRole, Guid>>();

    private async Task<EntityInfo> SeedEntityInfoAsync()
    {
        var entityInfo = new EntityInfo { Id = Guid.NewGuid(), Name = "Ticket", TypeName = typeof(Ticket).FullName!, IsDataAuthEnabled = true };
        DbContext.EntityInfos.Add(entityInfo);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return entityInfo;
    }

    private static EntityRole Rule(Guid entityInfoId, DataAuthOperation operation = DataAuthOperation.Query) => new()
    {
        Id = Guid.NewGuid(), EntityInfoId = entityInfoId, RoleId = RoleId, Operation = operation, IsEnabled = true
    };

    [Fact]
    public async Task TheDatabase_RejectsASecondRuleForTheSameEntityRoleAndOperation()
    {
        var entityInfo = await SeedEntityInfoAsync();
        DbContext.EntityRoles.Add(Rule(entityInfo.Id));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        DbContext.EntityRoles.Add(Rule(entityInfo.Id));

        await Should.ThrowAsync<DbUpdateException>(() => DbContext.SaveChangesAsync());
    }

    /// <summary>软删的规则不占位：删了再建同一条是正常操作。</summary>
    [Fact]
    public async Task ADeletedRule_DoesNotBlockRecreatingIt()
    {
        var entityInfo = await SeedEntityInfoAsync();
        var created = await Service.CreateEntityRoleAsync(new Dtos.CreateEntityRoleRequest
        {
            EntityInfoId = entityInfo.Id, RoleId = RoleId, Operation = DataAuthOperation.Query
        });
        (await Service.DeleteEntityRoleAsync(created.Data!.Id)).Succeeded.ShouldBeTrue();

        var again = await Service.CreateEntityRoleAsync(new Dtos.CreateEntityRoleRequest
        {
            EntityInfoId = entityInfo.Id, RoleId = RoleId, Operation = DataAuthOperation.Query
        });

        again.Succeeded.ShouldBeTrue(again.Message);
    }

    /// <summary>★ 判重之后、插入之前另一个请求建了同一条：答 409 而不是 500，且失败的实体不被下一次保存重放。</summary>
    [Fact]
    public async Task Create_LosingTheRace_Returns409_AndLeavesNothingToReplay()
    {
        var entityInfo = await SeedEntityInfoAsync();
        Repository.CompetingRow = Rule(entityInfo.Id);

        var result = await Service.CreateEntityRoleAsync(new Dtos.CreateEntityRoleRequest
        {
            EntityInfoId = entityInfo.Id, RoleId = RoleId, Operation = DataAuthOperation.Query
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        (await DbContext.EntityRoles.AsNoTracking().CountAsync()).ShouldBe(1);
        await Should.NotThrowAsync(() => DbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task BatchCreate_LosingTheRace_Returns409()
    {
        var entityInfo = await SeedEntityInfoAsync();
        Repository.CompetingRow = Rule(entityInfo.Id);

        var result = await Service.BatchCreateEntityRolesAsync(new Dtos.BatchEntityRoleRequest
        {
            EntityInfoId = entityInfo.Id, RoleIds = [RoleId, Guid.NewGuid()], Operation = DataAuthOperation.Query
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        await Should.NotThrowAsync(() => DbContext.SaveChangesAsync());
    }

    /// <summary>把一条规则的操作改成同一角色已有的那一档：与创建同一条 409。</summary>
    [Fact]
    public async Task Update_ToAnOperationTheRoleAlreadyHas_Returns409()
    {
        var entityInfo = await SeedEntityInfoAsync();
        var query = Rule(entityInfo.Id, DataAuthOperation.Query);
        var update = Rule(entityInfo.Id, DataAuthOperation.Update);
        DbContext.EntityRoles.AddRange(query, update);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var result = await Service.UpdateEntityRoleAsync(update.Id, new Dtos.UpdateEntityRoleRequest
        {
            Operation = DataAuthOperation.Query
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
    }

    /// <summary>在服务的判重之后、它自己的插入之前，经另一个 DbContext 提交一条相同的规则 —— 与并发请求的时序相同。</summary>
    private sealed class RacingEntityRoleRepository(DataAuthTestDbContext dbContext, IServiceProvider serviceProvider)
        : EFCoreRepository<DataAuthTestDbContext, EntityRole, Guid>(dbContext, serviceProvider: serviceProvider)
    {
        private readonly IServiceProvider _root = serviceProvider;

        public EntityRole? CompetingRow { get; set; }

        public override async Task InsertAsync(EntityRole entity, CancellationToken cancellationToken = default)
        {
            await InsertCompetitorAsync(cancellationToken);
            await base.InsertAsync(entity, cancellationToken);
        }

        public override async Task InsertManyAsync(IEnumerable<EntityRole> entities, CancellationToken cancellationToken = default)
        {
            await InsertCompetitorAsync(cancellationToken);
            await base.InsertManyAsync(entities, cancellationToken);
        }

        private async Task InsertCompetitorAsync(CancellationToken cancellationToken)
        {
            if (CompetingRow == null)
                return;

            using var scope = _root.CreateScope();
            var other = scope.ServiceProvider.GetRequiredService<DataAuthTestDbContext>();
            other.EntityRoles.Add(CompetingRow);
            await other.SaveChangesAsync(cancellationToken);
            CompetingRow = null;
        }
    }
}
