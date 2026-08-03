namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 预算门用的最小 DbContext（运行 + 绑定 + 运行时三张表）。
/// </summary>
public class CliBudgetDbContext : TnziDbContext<CliBudgetDbContext>
{
    public CliBudgetDbContext(DbContextOptions<CliBudgetDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CliRunConfiguration());
        modelBuilder.ApplyConfiguration(new CliAgentBindingConfiguration());
        modelBuilder.ApplyConfiguration(new CliRuntimeConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// USD 预算门必须同时守住外部执行域的两个入口。
/// </summary>
/// <remarks>
/// 外部执行按红线①不进 <c>AiMiddlewareContext</c> 管线，而内建路径的预算门恰恰长在管线里的
/// <c>QuotaMiddleware</c>。两条合起来的后果是：如果这里不显式设门，一个已经超预算的租户
/// 只要把 Agent 绑到外部 CLI 就能继续无限花钱 —— 而外部运行的量级是「几分钟到几小时」，
/// 是两条路里更贵的那条。这组测试钉的就是这个洞不会被重新打开。
/// </remarks>
public class CliBudgetGateTests : IntegratedTestBase<CliBudgetDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliBudgetDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliBudgetDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliBudgetDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliBudgetDbContext, CliRuntime, Guid>>();
    }

    private static Mock<IBudgetService> BudgetSaying(bool allowed, string? reason = null)
    {
        var mock = new Mock<IBudgetService>();
        mock.Setup(b => b.CheckBudgetAsync(
                It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BudgetCheckResult
            {
                IsAllowed = allowed,
                Status = allowed ? BudgetStatus.WithinBudget : BudgetStatus.BudgetExceeded,
                CurrentSpendUsd = allowed ? 1m : 250m,
                BudgetLimitUsd = 100m,
                Reason = reason
            });
        return mock;
    }

    private static IOptionsMonitor<CliAgentOptions> EnabledOptions()
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });
        return monitor.Object;
    }

    private async Task SeedBindingAsync()
    {
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId,
            HostId = "TEST-HOST",
            ProviderKey = "claude",
            Name = "claude @ TEST-HOST",
            ExecutablePath = "/usr/bin/claude",
            Status = CliRuntimeStatus.Online
        });
        await DbContext.SaveChangesAsync();
    }

    private CliAgentDispatcher CreateDispatcher(IBudgetService? budgetService)
        => new(
            ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            EnabledOptions(),
            ServiceProvider,
            budgetService);

    [Fact]
    public async Task Enqueue_WhenBudgetExhausted_IsRejectedAndNothingIsQueued()
    {
        await SeedBindingAsync();
        var dispatcher = CreateDispatcher(BudgetSaying(false, "Monthly budget of $100 exhausted").Object);

        var result = await dispatcher.EnqueueAsync(new CliRunRequestDto
        {
            AgentId = AgentId,
            Prompt = "burn some money"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(402);
        result.ErrorCode.ShouldBe(ErrorCodes.CliBudgetExceeded);
        result.Message.ShouldBe("Monthly budget of $100 exhausted");

        // 被拒的运行绝不能留在队列里 —— 留下就等于「先说不行，后台照跑」。
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Enqueue_WhenWithinBudget_IsQueued()
    {
        await SeedBindingAsync();
        var dispatcher = CreateDispatcher(BudgetSaying(true).Object);

        var result = await dispatcher.EnqueueAsync(new CliRunRequestDto
        {
            AgentId = AgentId,
            Prompt = "do the thing"
        });

        result.Succeeded.ShouldBeTrue();
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Enqueue_WhenNoBudgetServiceIsRegistered_IsQueued()
    {
        // 没有预算服务 = 这个部署根本没装预算能力，此时放行是正确答案而不是降级。
        await SeedBindingAsync();
        var dispatcher = CreateDispatcher(budgetService: null);

        var result = await dispatcher.EnqueueAsync(new CliRunRequestDto
        {
            AgentId = AgentId,
            Prompt = "do the thing"
        });

        result.Succeeded.ShouldBeTrue();
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(1);
    }

    private CliRunExecutor CreateExecutor(IBudgetService budgetService)
    {
        var runs = ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();

        // 预算门排在最前面，所以拒绝路径一个别的依赖都不会碰 —— 全给替身即可。
        // 反过来说：如果哪天有人把这道门挪到 setup 解析之后，这里就会开始 NRE，
        // 那本身就是「门被挪到了不该在的位置」的信号。
        return new CliRunExecutor(
            runs,
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            Mock.Of<ICliProviderRegistry>(),
            Mock.Of<ICliProtocolAdapterFactory>(),
            Mock.Of<ICliProcessHost>(),
            Mock.Of<ICliWorkspacePreparer>(),
            Mock.Of<ICliBriefComposer>(),
            Mock.Of<ICliMcpConfigComposer>(),
            new CliRunTokenService(runs, NullLogger<CliRunTokenService>.Instance),
            Mock.Of<ICliExecutableResolver>(),
            Mock.Of<IAgentGrantService>(),
            Mock.Of<ISkillService>(),
            new CliRunSignalHub(),
            EnabledOptions(),
            NullLogger<CliRunExecutor>.Instance,
            budgetService: budgetService);
    }

    [Fact]
    public async Task Execute_WhenBudgetWentExhaustedWhileQueued_FailsTheRunAsQuotaExceeded()
    {
        // 这是真正拦住花费的那道门。入队时查过一次不够 —— 队列可能积压很久，
        // 期间别的运行继续花钱，入队那一刻的答案到认领时早已不成立。
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            Status = CliRunStatus.Dispatched,
            Prompt = "expensive thing"
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();

        var executor = CreateExecutor(BudgetSaying(false, "Monthly budget of $100 exhausted").Object);
        await executor.ExecuteAsync(run.Id, CancellationToken.None);

        var stored = await DbContext.Set<CliRun>().AsNoTracking().FirstAsync(r => r.Id == run.Id);
        stored.Status.ShouldBe(CliRunStatus.Failed);
        stored.FailureReason.ShouldBe(CliRunFailureReason.QuotaExceeded);
        stored.Error.ShouldBe("Monthly budget of $100 exhausted");

        // 进程一次都没起 —— 拒绝必须发生在花钱之前，而不是跑完再记账。
        stored.StartedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Execute_ResolvesBudgetFromThePersistedRun_NotAmbientContext()
    {
        // 认领发生在后台服务里，那里没有 HTTP 请求也没有 ICurrentTenant。
        // 读环境只会拿到 null，于是每条运行都退化成「按全局预算算」，租户维度形同虚设。
        var tenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            Status = CliRunStatus.Dispatched,
            Prompt = "x",
            TenantId = tenantId
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();

        var budget = BudgetSaying(false, "exhausted");
        await CreateExecutor(budget.Object).ExecuteAsync(run.Id, CancellationToken.None);

        budget.Verify(b => b.CheckBudgetAsync(
            It.IsAny<Guid?>(), tenantId, AgentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enqueue_ChecksBudgetAgainstTheRequestedAgent()
    {
        // Agent 级预算覆盖要生效，Agent ID 就必须真的传下去 —— 传 null 会静默退化成
        // 「只按全局预算算」，而那正是一个看起来在工作的预算门最常见的失效方式。
        await SeedBindingAsync();
        var budget = BudgetSaying(true);
        var dispatcher = CreateDispatcher(budget.Object);

        await dispatcher.EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x" });

        budget.Verify(b => b.CheckBudgetAsync(
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), AgentId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
