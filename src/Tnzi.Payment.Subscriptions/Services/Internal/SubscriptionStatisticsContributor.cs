namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 支付统计里订阅那一半的供给方：活跃订阅数 + 整块订阅指标（MRR / 流失率 / ARPU / 计划分布）。
/// </summary>
/// <remarks>
/// 全部查询逐字搬自父模块拆分前的 <c>PaymentStatisticsService</c>：口径、分组维度、
/// 折算公式与四舍五入位数都没有改。父模块从此只算它真正拥有的那三块（支付、退款、促销）。
/// </remarks>
public class SubscriptionStatisticsContributor : IPaymentStatisticsContributor
{
    private readonly IRepository<Subscription, Guid> _subscriptionRepository;

    public SubscriptionStatisticsContributor(IRepository<Subscription, Guid> subscriptionRepository)
    {
        _subscriptionRepository = Check.NotNull(subscriptionRepository);
    }

    /// <inheritdoc />
    public async Task<int?> GetActiveSubscriptionCountAsync(CancellationToken cancellationToken = default)
    {
        return await _subscriptionRepository.AsNoTracking()
            .CountAsync(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SubscriptionMetricsDto?> GetSubscriptionMetricsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // 活跃与试用订阅数
        var activeCount = await _subscriptionRepository.AsNoTracking()
            .CountAsync(s => s.Status == SubscriptionStatus.Active, cancellationToken);
        var trialCount = await _subscriptionRepository.AsNoTracking()
            .CountAsync(s => s.Status == SubscriptionStatus.Trial, cancellationToken);

        // 本月新增订阅
        var newThisMonth = await _subscriptionRepository.AsNoTracking()
            .CountAsync(s => s.CreationTime >= monthStart
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial),
                cancellationToken);

        // 本月取消订阅
        var cancelledThisMonth = await _subscriptionRepository.AsNoTracking()
            .CountAsync(s => s.Status == SubscriptionStatus.Cancelled
                && s.CancelTime != null && s.CancelTime >= monthStart,
                cancellationToken);

        // 上月活跃数（用于流失率计算）：创建时间在本月前 + 当时活跃或本月才取消
        var lastMonthActive = await _subscriptionRepository.AsNoTracking()
            .CountAsync(s => s.CreationTime < monthStart
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial
                    || (s.Status == SubscriptionStatus.Cancelled && s.CancelTime >= monthStart)),
                cancellationToken);

        var churnRate = lastMonthActive > 0
            ? Math.Round((decimal)cancelledThisMonth / lastMonthActive * 100, 2)
            : 0;

        // MRR：活跃订阅的计划价格折算为月度等值金额。
        // 数据库级 GroupBy 按 (价格,周期类型,周期值) 归并，避免把每条活跃订阅都加载到内存（行数收敛到不同计划配置数）
        var mrrGroups = await _subscriptionRepository.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active && s.Plan != null)
            .GroupBy(s => new { s.Plan!.Price, s.Plan.CycleType, s.Plan.CycleValue })
            .Select(g => new { g.Key.Price, g.Key.CycleType, g.Key.CycleValue, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var mrr = mrrGroups.Sum(g => CalculateMonthlyEquivalent(g.Price, g.CycleType, g.CycleValue) * g.Count);

        // ARPU
        var arpu = activeCount > 0 ? Math.Round(mrr / activeCount, 2) : 0;

        // 计划分布（数据库级 GroupBy，通过导航属性 JOIN）
        var planDistribution = await _subscriptionRepository.AsNoTracking()
            .Where(s => (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial) && s.Plan != null)
            .GroupBy(s => s.Plan!.PlanName)
            .Select(g => new PlanDistributionDto
            {
                PlanName = g.Key,
                SubscriptionCount = g.Count(),
                Revenue = g.Sum(s => s.PaidAmount)
            })
            .OrderByDescending(p => p.SubscriptionCount)
            .ToListAsync(cancellationToken);

        return new SubscriptionMetricsDto
        {
            MonthlyRecurringRevenue = mrr,
            ActiveSubscriptions = activeCount,
            TrialSubscriptions = trialCount,
            NewSubscriptionsThisMonth = newThisMonth,
            CancelledThisMonth = cancelledThisMonth,
            ChurnRate = churnRate,
            AverageRevenuePerUser = arpu,
            PlanDistribution = planDistribution
        };
    }

    /// <summary>
    /// 将计划价格折算为月度等值金额
    /// </summary>
    private static decimal CalculateMonthlyEquivalent(decimal price, BillingCycleType cycleType, int cycleValue)
    {
        if (cycleValue <= 0) return 0;

        return cycleType switch
        {
            BillingCycleType.Day => price / cycleValue * 30,
            BillingCycleType.Week => price / cycleValue * (30m / 7),
            BillingCycleType.Month => price / cycleValue,
            BillingCycleType.Year => price / (cycleValue * 12),
            BillingCycleType.OneTime => 0,
            _ => 0
        };
    }
}
