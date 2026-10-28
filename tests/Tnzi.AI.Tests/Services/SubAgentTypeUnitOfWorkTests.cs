using Tnzi.AI.Tests.Integration;

namespace Tnzi.AI.Tests.Services;

/// <summary>
/// 全局工作单元下的子 Agent 类型 CRUD → 注册表重载回归测试（真实 SQLite + 真实 EFCoreRepository + UnitOfWorkManager）。
/// </summary>
/// <remarks>
/// <para>
/// 宿主开着 <c>EnableGlobalUnitOfWork</c> 时仓储延迟保存；CRUD 之后注册表按<b>数据库</b>整桶重载，
/// 而 EF 查询看不见变更跟踪器里尚未保存的增改删。于是接口返回 200，注册表却是请求之前的样子：
/// 新建的类型 spawn 时 404「Sub-agent type not found」，禁用或删除的类型照样能 spawn，直到下一次 CRUD 或重启。
/// </para>
/// <para>Mock 仓储不会延迟保存，这个缺陷只在真实 EF + UnitOfWorkManager 上出现。</para>
/// </remarks>
public class SubAgentTypeUnitOfWorkTests : IntegratedTestBase<AiIntegrationDbContext>
{
    private IUnitOfWorkManager Manager => ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
    private ISubAgentRegistry Registry => ServiceProvider.GetRequiredService<ISubAgentRegistry>();
    private ISubAgentTypeService Service => ServiceProvider.GetRequiredService<ISubAgentTypeService>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(AiIntegrationDbContext) });
        services.AddSingleton(_ => entityManagerMock.Object);

        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IRepository<SubAgentType, Guid>, EFCoreRepository<AiIntegrationDbContext, SubAgentType, Guid>>();
        services.AddSingleton<ISubAgentRegistry, SubAgentRegistry>();
        services.AddScoped<ISubAgentTypeService, SubAgentTypeService>();
    }

    private static SubAgentTypeInputDto Reviewer(bool isEnabled = true) => new()
    {
        Name = "reviewer",
        Description = "Reviews a change",
        ToolGroups = ["text"],
        MaxTurns = 10,
        IsEnabled = isEnabled
    };

    private SubAgentTypeDefinition? Lookup(string name) => Registry.GetForTenant(name, SubAgentTenantKey.Default);

    [Fact]
    public async Task Create_UnderGlobalUnitOfWork_IsSpawnableImmediately()
    {
        Manager.EnableTransaction();

        var created = await Service.CreateAsync(Reviewer());

        created.Succeeded.ShouldBeTrue(created.Message);
        Lookup("reviewer").ShouldNotBeNull("a type the API just reported as created must be resolvable by spawn_agent");

        await Manager.CommitTransactionAsync();
    }

    [Fact]
    public async Task Disable_UnderGlobalUnitOfWork_StopsBeingSpawnableImmediately()
    {
        Manager.EnableTransaction();
        var created = await Service.CreateAsync(Reviewer());
        Lookup("reviewer").ShouldNotBeNull();

        var updated = await Service.UpdateAsync(created.Data!.Id, Reviewer(isEnabled: false));

        updated.Succeeded.ShouldBeTrue(updated.Message);
        Lookup("reviewer").ShouldBeNull("a disabled type must not stay spawnable until the next CRUD");

        await Manager.CommitTransactionAsync();
    }

    [Fact]
    public async Task Delete_UnderGlobalUnitOfWork_StopsBeingSpawnableImmediately()
    {
        Manager.EnableTransaction();
        var created = await Service.CreateAsync(Reviewer());
        Lookup("reviewer").ShouldNotBeNull();

        var deleted = await Service.DeleteAsync(created.Data!.Id);

        deleted.Succeeded.ShouldBeTrue(deleted.Message);
        Lookup("reviewer").ShouldBeNull("a deleted type must not stay spawnable until the next CRUD");

        await Manager.CommitTransactionAsync();
    }

    [Fact]
    public async Task Create_WritesInsideTheTransaction_SoRollbackUndoesTheRow()
    {
        // flush 走工作单元，不绕开事务：回滚的请求不能留下一条撤不回来的类型定义
        Manager.EnableTransaction();
        await Service.CreateAsync(Reviewer());
        await Manager.RollbackTransactionAsync();

        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<SubAgentType>().AsNoTracking().AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Create_WithADuplicateName_Returns409InsteadOfAUniqueIndexFailure()
    {
        (await Service.CreateAsync(Reviewer())).Succeeded.ShouldBeTrue();

        var duplicate = Reviewer();
        duplicate.Name = "Reviewer";
        var result = await Service.CreateAsync(duplicate);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        result.ErrorCode.ShouldBe(ErrorCodes.SubAgentTypeAlreadyExists);
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<SubAgentType>().AsNoTracking().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Update_RenamingOntoAnotherType_Returns409AndLeavesBothRowsIntact()
    {
        (await Service.CreateAsync(Reviewer())).Succeeded.ShouldBeTrue();
        var other = Reviewer();
        other.Name = "planner";
        var planner = await Service.CreateAsync(other);

        var rename = Reviewer();
        var result = await Service.UpdateAsync(planner.Data!.Id, rename);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<SubAgentType>().AsNoTracking().Select(t => t.Name).ToListAsync())
            .ShouldBe(["reviewer", "planner"], ignoreOrder: true);
    }

    [Fact]
    public async Task Update_KeepingItsOwnName_IsNotAConflict()
    {
        var created = await Service.CreateAsync(Reviewer());

        var same = Reviewer();
        same.Description = "Reviews a change carefully";
        var result = await Service.UpdateAsync(created.Data!.Id, same);

        result.Succeeded.ShouldBeTrue(result.Message);
    }
}
