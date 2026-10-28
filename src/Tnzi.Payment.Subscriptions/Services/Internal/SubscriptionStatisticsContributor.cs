namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 支付统计里订阅那一半的供给方：活跃订阅数 + 整块订阅指标（MRR / 流失率 / ARPU / 计划分布）。
/// </summary>
/// <remarks>
/// <para>
/// 查询搬自父模块拆分前的 <c>PaymentStatisticsService</c>，折算公式与四舍五入位数未变。
/// 父模块从此只算它真正拥有的那三块（支付、退款、促销）。
/// </para>
/// <para>
/// ★ <b>口径：「活跃」恒指 <c>Status == Active</c></b>，试用单独计数。
/// <c>ActiveSubscriptions</c>（总览与指标两处）、<c>PlanDistribution</c>、MRR 与 ARPU 的分母
/// 全部按这一条，因此计划分布加总回得到活跃订阅数，ARPU 与它同分母。
/// 两个例外是显式的：<c>NewSubscriptionsThisMonth</c> 衡量获客（一次试用开通就是一次获客），
/// 流失率的分子分母都含试用（分子数的是所有取消，分母排掉试用会让比率系统性偏高）。
/// </para>
/// <para>
/// ★ <b>流失率口径：分子 = 本月提出的取消，分母 = 月初在册的订阅。</b>
/// 「取消」按 <c>CancelTime</c> 落在本月计，不看现在的状态：默认的「到期取消」把订阅置为
/// <c>PendingRenewal</c>、到点由过期扫描置为 <c>Expired</c>，从来不会成为 <c>Cancelled</c>。
/// 恢复订阅会清掉 <c>CancelTime</c>，所以撤回的取消不计。欠费到期（没人提出取消）不在分子里：
/// 它是非自愿流失，与「取消」是两件事。分子分母都只数真正开始过的订阅（开过试用或付过订阅款）：
/// 首期单没付成而被系统收口的 <c>Pending</c> 同样带着 <c>CancelTime</c>，它们不是订户，结束也不是流失。
/// </para>
/// </remarks>
public class SubscriptionStatisticsContributor : IPaymentStatisticsContributor
{
    private readonly IRepository<Subscription, Guid> _subscriptionRepository;

    public SubscriptionStatisticsContributor(IRepository<Subscription, Guid> subscriptionRepository)
    {
        _subscriptionRepository = Check.NotNull(subscriptionRepository);
    }

    /// <inheritdoc />
    /// <remarks>
    /// ★ 「活跃订阅数」在本模块里恒指 <c>Status == Active</c>，试用单独计数。
    /// 此前这里算的是 Active + Trial，而<b>同一份看板上</b>的订阅指标端点算的是 Active ——
    /// 同一张 KPI 卡片在两个数字之间跳，而两处都没错到能被发现的地步。
    /// 试用不产生收入，把它计入「活跃」还会让 MRR / ARPU 的分母与这个数对不上。
    /// 口径见类注释。
    /// </remarks>
    public async Task<int?> GetActiveSubscriptionCountAsync(CancellationToken cancellationToken = default)
    {
        return await _subscriptionRepository.AsNoTracking()
            .CountAsync(s => s.Status == SubscriptionStatus.Active, cancellationToken);
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

        // 本月新增订阅。★ 这一项**刻意**把 Trial 算进来，与上面的口径不同也不矛盾：
        // 它衡量的是获客，而一次试用开通就是一次获客。留意区别，别顺手改成 Active-only。
        var newThisMonth = await _subscriptionRepository.AsNoTracking()
            .CountAsync(s => s.CreationTime >= monthStart
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial),
                cancellationToken);

        // 本月取消订阅：按「本月提出取消」计，不论现在是什么状态。
        // ★ 此前只数 Status == Cancelled，而默认的「到期取消」把订阅置为 PendingRenewal、到点再变 Expired，
        //   永远不会成为 Cancelled，本月按默认方式退订的人一个都不进流失率，看板上可以是 0%。
        // ★ 只数真正开始过的订阅（开过试用或付过订阅款）：首期单没付成的 Pending 被系统收口为 Cancelled 时同样带着
        //   CancelTime，算进来就是把每一次结账放弃记成一次流失。判据与「是否订阅过」的探针、恢复守卫同一个。
        var cancelledThisMonth = await _subscriptionRepository.AsNoTracking()
            .CountAsync(s => s.CancelTime != null && s.CancelTime >= monthStart
                && (s.TrialStartTime != null || (s.LastBillingTradeNo != null && s.LastBillingTradeNo != "") || s.PaidAmount > 0),
                cancellationToken);

        // 月初在册数（流失率分母）：创建在本月之前，且月初时还没结束：
        // 现在仍是非终态（含等续费、欠费、暂停），或本月才提出取消、本月才到期。
        // ★ 同样刻意包含 Trial：分子 cancelledThisMonth 数的是**所有**取消（含试用期内退出），
        // 分母排掉试用会让比率系统性偏高。分子分母同口径优先于与 ActiveSubscriptions 同口径。
        // 同样只数真正开始过的（与分子同一个判据）：上月建单、首期单本月才过期的 Pending 月初并不在册。
        var lastMonthActive = await _subscriptionRepository.AsNoTracking()
            .CountAsync(s => s.CreationTime < monthStart
                && (s.TrialStartTime != null || (s.LastBillingTradeNo != null && s.LastBillingTradeNo != "") || s.PaidAmount > 0)
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial
                    || s.Status == SubscriptionStatus.PendingRenewal || s.Status == SubscriptionStatus.PastDue
                    || s.Status == SubscriptionStatus.Suspended
                    || s.Status == SubscriptionStatus.Paused
                    || (s.CancelTime != null && s.CancelTime >= monthStart)
                    || (s.Status == SubscriptionStatus.Expired && s.EndTime != null && s.EndTime >= monthStart)),
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

        // 计划分布（数据库级 GroupBy，通过导航属性 JOIN）。
        // 只数 Active：分布要能加总回上面那个 ActiveSubscriptions，也要与 MRR / ARPU 的口径一致 ——
        // 此前它连 Trial 一起数，于是各计划人数之和比标题上的活跃订阅数大，而没有任何说明。
        var planDistribution = await _subscriptionRepository.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active && s.Plan != null)
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
