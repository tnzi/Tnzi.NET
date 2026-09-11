using Tnzi.AI.Tests.Integration;

namespace Tnzi.AI.Tests.Services;

/// <summary>
/// 全局工作单元下的权限规则 CRUD → 评估器刷新回归测试（真实 SQLite + 真实 EFCoreRepository + UnitOfWorkManager）。
/// </summary>
/// <remarks>
/// <para>
/// 宿主开着 <c>EnableGlobalUnitOfWork</c> 时仓储延迟保存，新建的规则行只停在变更跟踪器里；
/// 而 <c>ConfiguredToolPermissionEvaluator.RefreshDbRulesAsync</c> 会<b>另开一个作用域、另开一个
/// DbContext</b> 重新查询 —— 既看不见跟踪器里的行，即便已 flush 也看不见另一条连接上未提交的写入。
/// 于是刚建的 Deny 规则在下一次 CRUD 或进程重启前完全不生效，而接口返回的是 200 成功。
/// </para>
/// <para>
/// 这些用例必须跑在真实 EF + UnitOfWorkManager 上：Mock 仓储不会延迟保存，这个缺陷在 Mock 下不存在。
/// </para>
/// </remarks>
public class ToolPermissionRuleUnitOfWorkTests : IntegratedTestBase<AiIntegrationDbContext>
{
    private IUnitOfWorkManager Manager => ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var mapperConfig = new TypeAdapterConfig();
        MapperExtensions.SetMapper(new Mapper(mapperConfig));

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(AiIntegrationDbContext) });
        services.AddSingleton(_ => entityManagerMock.Object);

        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IRepository<ToolPermissionRuleEntity, Guid>,
            EFCoreRepository<AiIntegrationDbContext, ToolPermissionRuleEntity, Guid>>();
        services.AddScoped<IToolPermissionRuleStore, DatabaseToolPermissionRuleStore>();

        services.AddSingleton<IOptionsMonitor<AIOptions>>(
            new StaticOptionsMonitor<AIOptions>(new AIOptions()));

        services.AddScoped<IToolPermissionRuleService, ToolPermissionRuleService>();
    }

    /// <summary>
    /// 现实接线：评估器自己带作用域工厂（生产就是这样注册的）。
    /// </summary>
    private ConfiguredToolPermissionEvaluator CreateEvaluatorWithOwnScope() =>
        new(ServiceProvider.GetRequiredService<IOptionsMonitor<AIOptions>>(),
            ServiceProvider.GetRequiredService<IServiceScopeFactory>());

    /// <summary>
    /// 评估器<b>够不着数据库</b>的接线。它替代的是生产里真正发生的事：评估器另开的那条连接
    /// 看不见本请求事务里尚未提交的写入。规则若仍能立刻生效，只可能是 CRUD 侧在自己的
    /// 作用域里读好再推给评估器 —— 这一条把「同一个作用域读」钉成必须项。
    /// </summary>
    private ConfiguredToolPermissionEvaluator CreateEvaluatorWithoutDbAccess() =>
        new(ServiceProvider.GetRequiredService<IOptionsMonitor<AIOptions>>(), scopeFactory: null);

    private IToolPermissionRuleService CreateService(IToolPermissionEvaluator evaluator) =>
        ActivatorUtilities.CreateInstance<ToolPermissionRuleService>(ServiceProvider, evaluator);

    private static CreatePersistedPermissionRuleDto DenyBash() => new()
    {
        ToolPattern = "bash",
        Behavior = PermissionBehavior.Deny,
        Scope = ToolPermissionScope.System,
        Priority = 100,
        Reason = "Blocked by administrator",
        IsEnabled = true
    };

    [Fact]
    public async Task CreateRule_UnderGlobalUnitOfWork_TakesEffectImmediately()
    {
        using var evaluator = CreateEvaluatorWithOwnScope();
        var service = CreateService(evaluator);
        Manager.EnableTransaction();

        var result = await service.CreateAsync(DenyBash());
        result.Succeeded.ShouldBeTrue();

        var decision = evaluator.Evaluate(new ToolPermissionContext { ToolName = "bash" });
        decision.Behavior.ShouldBe(
            PermissionBehavior.Deny,
            "a rule the API just reported as created must not be inert until the next CRUD or a restart");

        await Manager.CommitTransactionAsync();
    }

    [Fact]
    public async Task CreateRule_WhenTheEvaluatorCannotReachTheDatabaseItself_StillTakesEffect()
    {
        using var evaluator = CreateEvaluatorWithoutDbAccess();
        var service = CreateService(evaluator);
        Manager.EnableTransaction();

        await service.CreateAsync(DenyBash());

        evaluator.Evaluate(new ToolPermissionContext { ToolName = "bash" })
            .Behavior.ShouldBe(PermissionBehavior.Deny);

        await Manager.CommitTransactionAsync();
    }

    [Fact]
    public async Task UpdateRule_UnderGlobalUnitOfWork_TakesEffectImmediately()
    {
        using var evaluator = CreateEvaluatorWithoutDbAccess();
        var service = CreateService(evaluator);
        Manager.EnableTransaction();

        var created = await service.CreateAsync(DenyBash());
        created.Data.ShouldNotBeNull();
        evaluator.Evaluate(new ToolPermissionContext { ToolName = "bash" })
            .Behavior.ShouldBe(PermissionBehavior.Deny);

        var relaxed = DenyBash();
        relaxed.Behavior = PermissionBehavior.Allow;
        await service.UpdateAsync(created.Data!.Id, relaxed);

        evaluator.Evaluate(new ToolPermissionContext { ToolName = "bash" })
            .Behavior.ShouldBe(PermissionBehavior.Allow);

        await Manager.CommitTransactionAsync();
    }

    [Fact]
    public async Task DeleteRule_UnderGlobalUnitOfWork_TakesEffectImmediately()
    {
        using var evaluator = CreateEvaluatorWithoutDbAccess();
        var service = CreateService(evaluator);
        Manager.EnableTransaction();

        var created = await service.CreateAsync(DenyBash());
        created.Data.ShouldNotBeNull();
        evaluator.Evaluate(new ToolPermissionContext { ToolName = "bash" })
            .Behavior.ShouldBe(PermissionBehavior.Deny);

        await service.DeleteAsync(created.Data!.Id);

        evaluator.Evaluate(new ToolPermissionContext { ToolName = "bash" })
            .Behavior.ShouldBe(PermissionBehavior.Allow);

        await Manager.CommitTransactionAsync();
    }

    [Fact]
    public async Task CreateRule_WritesInsideTheTransaction_SoRollbackUndoesIt()
    {
        // 让新行立刻可见的正确做法是经工作单元 flush，不是绕开事务直接落库：
        // 后者会让一次失败的请求留下一条无法回滚的权限规则
        using var evaluator = CreateEvaluatorWithoutDbAccess();
        var service = CreateService(evaluator);
        Manager.EnableTransaction();

        await service.CreateAsync(DenyBash());
        await Manager.RollbackTransactionAsync();

        DbContext.ChangeTracker.Clear();
        var rows = await DbContext.Set<ToolPermissionRuleEntity>().AsNoTracking().ToListAsync();
        rows.ShouldBeEmpty();
    }
}
