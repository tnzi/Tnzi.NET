namespace Tnzi.AI.Tests;

/// <summary>
/// BudgetService 集成测试 DbContext
/// </summary>
public class BudgetDbContext : TnziDbContext<BudgetDbContext>
{
    public BudgetDbContext(
        DbContextOptions<BudgetDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AgentConfiguration());
        modelBuilder.ApplyConfiguration(new UsageLogConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// BudgetService 集成测试基类
/// </summary>
public abstract class BudgetTestBase : IntegratedTestBase<BudgetDbContext>
{
    protected readonly IBudgetService Service;
    protected readonly AIOptions AiOptions;

    protected BudgetTestBase()
    {
        // 清除静态缓存，避免跨测试类干扰
        BudgetService.ClearCacheForTesting();

        AiOptions = new AIOptions
        {
            Budget = new BudgetOptions
            {
                Enabled = true,
                DefaultMonthlyBudgetUsd = 100m,
                WarningThreshold = 0.8,
                CacheTtlSeconds = 0 // 测试中禁用缓存
            }
        };

        var monitor = new Mock<IOptionsMonitor<AIOptions>>();
        monitor.Setup(x => x.CurrentValue).Returns(AiOptions);

        Service = new BudgetService(
            ServiceProvider,
            ServiceProvider.GetRequiredService<IRepository<UsageLog, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<Agent, Guid>>(),
            monitor.Object);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        services.AddScoped<IRepository<UsageLog, Guid>,
            EFCoreRepository<BudgetDbContext, UsageLog, Guid>>();
        services.AddScoped<IRepository<Agent, Guid>,
            EFCoreRepository<BudgetDbContext, Agent, Guid>>();
    }

    protected async Task SeedLogsAsync(params UsageLog[] logs)
    {
        DbContext.Set<UsageLog>().AddRange(logs);
        await DbContext.SaveChangesAsync();
    }

    protected async Task SeedAgentAsync(Agent agent)
    {
        DbContext.Set<Agent>().Add(agent);
        await DbContext.SaveChangesAsync();
    }

    protected static UsageLog CreateLog(decimal? costUsd, Guid? agentId = null, Guid? tenantId = null, DateTime? creationTime = null)
    {
        return new UsageLog
        {
            Id = Guid.NewGuid(),
            Provider = "OpenAI",
            Model = "gpt-4o",
            OperationType = "Chat",
            InputTokens = 100,
            OutputTokens = 50,
            TotalTokens = 150,
            DurationMs = 500,
            IsSuccess = true,
            EstimatedCostUsd = costUsd,
            AgentId = agentId,
            TenantId = tenantId,
            CreationTime = creationTime ?? DateTime.UtcNow
        };
    }
}

// -------------------------------------------------------------------------
// Within Budget
// -------------------------------------------------------------------------

public class BudgetService_WithinBudget : BudgetTestBase
{
    [Fact]
    public async Task CheckBudget_WhenSpendBelowThreshold_ReturnsWithinBudget()
    {
        // 花费 $20 / $100 限额 = 20%
        await SeedLogsAsync(
            CreateLog(10m),
            CreateLog(10m));

        var result = await Service.CheckBudgetAsync(null, null, null);

        result.IsAllowed.ShouldBeTrue();
        result.Status.ShouldBe(BudgetStatus.WithinBudget);
        result.CurrentSpendUsd.ShouldBe(20m);
        result.BudgetLimitUsd.ShouldBe(100m);
    }
}

public class BudgetService_NoLogs : BudgetTestBase
{
    [Fact]
    public async Task CheckBudget_WhenNoLogs_ReturnsWithinBudget()
    {
        var result = await Service.CheckBudgetAsync(null, null, null);

        result.IsAllowed.ShouldBeTrue();
        result.Status.ShouldBe(BudgetStatus.WithinBudget);
        result.CurrentSpendUsd.ShouldBe(0m);
    }
}

// -------------------------------------------------------------------------
// Warning Threshold
// -------------------------------------------------------------------------

public class BudgetService_Warning : BudgetTestBase
{
    [Fact]
    public async Task CheckBudget_WhenSpendAtWarningThreshold_ReturnsWarning()
    {
        // 花费 $85 / $100 限额 = 85% > 80% warning
        await SeedLogsAsync(
            CreateLog(50m),
            CreateLog(35m));

        var result = await Service.CheckBudgetAsync(null, null, null);

        result.IsAllowed.ShouldBeTrue();
        result.Status.ShouldBe(BudgetStatus.WarningThreshold);
        result.Reason.ShouldNotBeNullOrEmpty();
    }
}

// -------------------------------------------------------------------------
// Budget Exceeded
// -------------------------------------------------------------------------

public class BudgetService_Exceeded : BudgetTestBase
{
    [Fact]
    public async Task CheckBudget_WhenSpendExceedsBudget_ReturnsBudgetExceeded()
    {
        // 花费 $110 / $100 限额 = 110%
        await SeedLogsAsync(
            CreateLog(60m),
            CreateLog(50m));

        var result = await Service.CheckBudgetAsync(null, null, null);

        result.IsAllowed.ShouldBeFalse();
        result.Status.ShouldBe(BudgetStatus.BudgetExceeded);
        var reason = result.Reason;
        reason.ShouldNotBeNull();
        reason.ShouldContain("exceeded");
    }
}

// -------------------------------------------------------------------------
// Per-Agent Budget
// -------------------------------------------------------------------------

public class BudgetService_PerAgent : BudgetTestBase
{
    [Fact]
    public async Task CheckBudget_WhenAgentBudgetExceeded_RejectsEvenIfGlobalBudgetOk()
    {
        var agentId = Guid.NewGuid();
        var agent = new Agent { Id = agentId, Name = "expensive-agent" };
        await SeedAgentAsync(agent);

        // 给该 Agent 设置 $10 预算
        AiOptions.Budget.PerAgentBudgets["expensive-agent"] = 10m;

        // Agent 花费 $15
        await SeedLogsAsync(
            CreateLog(15m, agentId: agentId));

        var result = await Service.CheckBudgetAsync(null, null, agentId);

        result.IsAllowed.ShouldBeFalse();
        result.Status.ShouldBe(BudgetStatus.BudgetExceeded);
        var reason = result.Reason;
        reason.ShouldNotBeNull();
        reason.ShouldContain("Agent budget exceeded");
    }

    [Fact]
    public async Task CheckBudget_WhenAgentBudgetOk_Allows()
    {
        var agentId = Guid.NewGuid();
        var agent = new Agent { Id = agentId, Name = "cheap-agent" };
        await SeedAgentAsync(agent);

        AiOptions.Budget.PerAgentBudgets["cheap-agent"] = 50m;

        await SeedLogsAsync(
            CreateLog(5m, agentId: agentId));

        var result = await Service.CheckBudgetAsync(null, null, agentId);

        result.IsAllowed.ShouldBeTrue();
    }
}

// -------------------------------------------------------------------------
// Disabled
// -------------------------------------------------------------------------

public class BudgetService_Disabled : BudgetTestBase
{
    [Fact]
    public async Task CheckBudget_WhenDisabled_AlwaysAllows()
    {
        AiOptions.Budget.Enabled = false;

        // 即使花费超限也允许
        await SeedLogsAsync(
            CreateLog(500m));

        var result = await Service.CheckBudgetAsync(null, null, null);

        result.IsAllowed.ShouldBeTrue();
        result.Status.ShouldBe(BudgetStatus.WithinBudget);
    }
}

// -------------------------------------------------------------------------
// Summary
// -------------------------------------------------------------------------

public class BudgetService_Summary : BudgetTestBase
{
    [Fact]
    public async Task GetSummary_ReturnsCorrectAggregation()
    {
        var agentId = Guid.NewGuid();
        var agent = new Agent { Id = agentId, Name = "test-agent" };
        await SeedAgentAsync(agent);

        await SeedLogsAsync(
            CreateLog(10m, agentId: agentId),
            CreateLog(20m, agentId: agentId),
            CreateLog(5m)); // 无 Agent

        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);

        var summary = await Service.GetSummaryAsync(null, start, end);

        summary.CurrentSpendUsd.ShouldBe(35m);
        summary.BudgetLimitUsd.ShouldBe(100m);
        summary.ByAgent.Count.ShouldBe(1);
        summary.ByAgent[0].AgentName.ShouldBe("test-agent");
        summary.ByAgent[0].SpendUsd.ShouldBe(30m);
        summary.ByAgent[0].RequestCount.ShouldBe(2);
    }
}

// -------------------------------------------------------------------------
// Update Spend (cache invalidation)
// -------------------------------------------------------------------------

public class BudgetService_UpdateSpend : BudgetTestBase
{
    [Fact]
    public async Task UpdateSpend_InvalidatesCache_NextCheckReflectsNewData()
    {
        await SeedLogsAsync(CreateLog(50m));

        // 第一次检查
        var result1 = await Service.CheckBudgetAsync(null, null, null);
        result1.CurrentSpendUsd.ShouldBe(50m);

        // 添加更多花费
        await SeedLogsAsync(CreateLog(40m));

        // 失效缓存
        await Service.UpdateSpendAsync(null, null, null, 40m);

        // 第二次检查应该反映新数据
        var result2 = await Service.CheckBudgetAsync(null, null, null);
        result2.CurrentSpendUsd.ShouldBe(90m);
        result2.Status.ShouldBe(BudgetStatus.WarningThreshold);
    }
}

// -------------------------------------------------------------------------
// Previous month logs excluded
// -------------------------------------------------------------------------

public class BudgetService_PeriodBoundary : BudgetTestBase
{
    [Fact]
    public async Task CheckBudget_ExcludesPreviousMonthLogs()
    {
        var now = DateTime.UtcNow;
        var lastMonth = now.AddMonths(-1);

        // 上个月花了 $200（应被排除）
        await SeedLogsAsync(
            CreateLog(200m, creationTime: lastMonth));

        // 本月花了 $10
        await SeedLogsAsync(
            CreateLog(10m, creationTime: now));

        var result = await Service.CheckBudgetAsync(null, null, null);

        result.IsAllowed.ShouldBeTrue();
        result.CurrentSpendUsd.ShouldBe(10m);
    }
}
