using System.Reflection;

namespace Tnzi.AI.Tests;

/// <summary>
/// 预算已启用但成本数据根本没被记录时的行为测试。
/// </summary>
/// <remarks>
/// <para>
/// <c>AI:Budget:Enabled</c> 与 <c>AI:CostTracking:Enabled</c> 是同组的两个热设置，而费率
/// （<c>ModelCosts</c> / <c>DefaultCostRate</c>）不是热设置。只开预算不开成本追踪（或开了却一条费率都没配），
/// <c>EstimatedCostUsd</c> 恒为 null → 聚合恒为 0 → <c>CheckBudgetAsync</c> 恒答 WithinBudget，
/// 而界面显示的是「已启用、用量 0%」—— 与「这个月真没花钱」逐字相同的外观，
/// 一道花钱的闸门就此静默失效。
/// </para>
/// <para>
/// 判据取自<b>数据</b>而不是配置：成本也可以由消费方经
/// <c>IUsageLogService.LogUsageAsync(..., estimatedCostUsd, ...)</c> 直接写入，
/// 那种部署里 <c>CostTracking:Enabled=false</c> 的预算是能正常工作的。
/// </para>
/// </remarks>
public class BudgetService_CostDataMissing : BudgetTestBase
{
    [Fact]
    public async Task CheckBudget_WhenUsageExistsButNoRowCarriesCost_AnswersIndeterminate()
    {
        // 有用量、但一条成本都没记下来：0 美元是「量不出来」，不是「没花钱」
        await SeedLogsAsync(
            CreateLog(costUsd: null),
            CreateLog(costUsd: null));

        var result = await Service.CheckBudgetAsync(null, null, null);

        result.Status.ShouldBe(BudgetStatus.Indeterminate);
        result.Reason.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckBudget_WhenCostCannotBeMeasured_StillAllowsTheRequest()
    {
        // 一个勾选框不该让所有 AI 请求当场停摆：如实报告状态，放行交由宿主决定
        await SeedLogsAsync(CreateLog(costUsd: null));

        var result = await Service.CheckBudgetAsync(null, null, null);

        result.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckBudget_WhenSomeRowsCarryCost_EvaluatesNormally()
    {
        // 部分记账（例如刚打开成本追踪）不算「量不出来」
        await SeedLogsAsync(
            CreateLog(costUsd: null),
            CreateLog(10m));

        var result = await Service.CheckBudgetAsync(null, null, null);

        result.Status.ShouldBe(BudgetStatus.WithinBudget);
        result.CurrentSpendUsd.ShouldBe(10m);
    }

    [Fact]
    public async Task CheckBudget_WhenThereIsNoUsageAtAll_StaysWithinBudget()
    {
        // 空月份是真的没花钱，不是量不出来 —— 不能把它也报成无法判定
        var result = await Service.CheckBudgetAsync(null, null, null);

        result.Status.ShouldBe(BudgetStatus.WithinBudget);
        result.CurrentSpendUsd.ShouldBe(0m);
    }

    [Fact]
    public async Task CheckBudget_WhenBudgetIsDisabled_StaysWithinBudget()
    {
        AiOptions.Budget.Enabled = false;
        await SeedLogsAsync(CreateLog(costUsd: null));

        var result = await Service.CheckBudgetAsync(null, null, null);

        result.Status.ShouldBe(BudgetStatus.WithinBudget);
    }

    [Fact]
    public async Task GetSummary_WhenUsageExistsButNoRowCarriesCost_ReportsIndeterminate()
    {
        // 管理端的摘要必须与运行时的判断同源，否则界面上那个 0% 会一直安抚人
        await SeedLogsAsync(
            CreateLog(costUsd: null),
            CreateLog(costUsd: null));

        var summary = await Service.GetSummaryAsync(
            null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        summary.Status.ShouldBe(BudgetStatus.Indeterminate);
        summary.CurrentSpendUsd.ShouldBe(0m);
    }

    [Fact]
    public async Task GetSummary_WhenSomeRowsCarryCost_ReportsNormally()
    {
        await SeedLogsAsync(
            CreateLog(costUsd: null),
            CreateLog(10m));

        var summary = await Service.GetSummaryAsync(
            null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        summary.Status.ShouldBe(BudgetStatus.WithinBudget);
        summary.CurrentSpendUsd.ShouldBe(10m);
    }
}

/// <summary>
/// 启动期的预算 / 成本追踪交叉校验。
/// </summary>
/// <remarks>
/// 运行期的 Indeterminate 判据要等到本周期出现用量才成立；全新部署里没有用量，
/// 那道判据什么也说不出来，而配置已经注定这道闸门永远不会触发。启动期这一条补上那个窗口。
/// </remarks>
public class BudgetCostWiringValidationTests
{
    [Fact]
    public void BudgetEnabledWithoutCostTracking_Warns()
    {
        var logger = Validate(budgetEnabled: true, costTrackingEnabled: false, withRate: false);

        logger.Warnings.ShouldContain(w => w.Contains("AI:CostTracking:Enabled", StringComparison.Ordinal));
    }

    [Fact]
    public void BudgetAndCostTrackingEnabledButNoRateConfigured_Warns()
    {
        var logger = Validate(budgetEnabled: true, costTrackingEnabled: true, withRate: false);

        logger.Warnings.ShouldContain(w => w.Contains("ModelCosts", StringComparison.Ordinal));
    }

    [Fact]
    public void BudgetAndCostTrackingWiredTogether_StaysSilent()
    {
        var logger = Validate(budgetEnabled: true, costTrackingEnabled: true, withRate: true);

        logger.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void BudgetDisabled_StaysSilent()
    {
        // 预算关着时成本追踪配不配是宿主自己的事，不该借这个位置催他
        var logger = Validate(budgetEnabled: false, costTrackingEnabled: false, withRate: false);

        logger.Warnings.ShouldBeEmpty();
    }

    private static CapturingLogger Validate(bool budgetEnabled, bool costTrackingEnabled, bool withRate)
    {
        var aiOptions = new AIOptions();
        aiOptions.Budget.Enabled = budgetEnabled;
        aiOptions.CostTracking.Enabled = costTrackingEnabled;
        if (withRate)
        {
            aiOptions.CostTracking.ModelCosts["OpenAI:gpt-4o"] = new ModelCostRate
            {
                InputCostPer1MTokens = 2.5m,
                OutputCostPer1MTokens = 10m
            };
        }

        var services = new ServiceCollection();
        services.AddSingleton<IOptions<AIOptions>>(new OptionsWrapper<AIOptions>(aiOptions));
        using var provider = services.BuildServiceProvider();

        var logger = new CapturingLogger();
        var method = typeof(AIModule).GetMethod(
            "ValidateBudgetCostWiring",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.ShouldNotBeNull();
        method.Invoke(null, [provider, logger]);

        return logger;
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
