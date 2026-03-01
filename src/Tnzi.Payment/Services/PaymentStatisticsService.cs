namespace Tnzi.Payment.Services;

/// <summary>
/// 支付统计服务实现
/// </summary>
public class PaymentStatisticsService : ApplicationService, IPaymentStatisticsService
{
    private readonly IRepository<PaymentEntity, Guid> _paymentRepository;
    private readonly IRepository<Refund, Guid> _refundRepository;
    private readonly IRepository<Subscription, Guid> _subscriptionRepository;
    private readonly IRepository<SubscriptionPlan, Guid> _planRepository;
    private readonly IRepository<CouponUsage, Guid> _couponUsageRepository;
    private readonly IRepository<Promotion, Guid> _promotionRepository;

    public PaymentStatisticsService(
        IRepository<PaymentEntity, Guid> paymentRepository,
        IRepository<Refund, Guid> refundRepository,
        IRepository<Subscription, Guid> subscriptionRepository,
        IRepository<SubscriptionPlan, Guid> planRepository,
        IRepository<CouponUsage, Guid> couponUsageRepository,
        IRepository<Promotion, Guid> promotionRepository,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _paymentRepository = Check.NotNull(paymentRepository);
        _refundRepository = Check.NotNull(refundRepository);
        _subscriptionRepository = Check.NotNull(subscriptionRepository);
        _planRepository = Check.NotNull(planRepository);
        _couponUsageRepository = Check.NotNull(couponUsageRepository);
        _promotionRepository = Check.NotNull(promotionRepository);
    }

    public async Task<Result<PaymentStatisticsDto>> GetStatisticsAsync(StatisticsQueryDto query, CancellationToken cancellationToken = default)
    {
        var endTime = query.EndTime ?? DateTime.UtcNow;
        var startTime = query.StartTime ?? endTime.AddDays(-30);

        // 数据库级聚合：支付统计
        var paymentQuery = _paymentRepository.AsNoTracking()
            .Where(p => p.CreationTime >= startTime && p.CreationTime <= endTime);

        var totalTransactions = await paymentQuery.CountAsync(cancellationToken);
        var successfulTransactions = await paymentQuery.CountAsync(p => p.Status == PaymentStatus.Succeeded, cancellationToken);
        var failedTransactions = await paymentQuery.CountAsync(p => p.Status == PaymentStatus.Failed, cancellationToken);
        var totalRevenue = await paymentQuery
            .Where(p => p.Status == PaymentStatus.Succeeded)
            .SumAsync(p => (decimal?)p.PaidAmount, cancellationToken) ?? 0;

        // 数据库级聚合：退款统计
        var refundQuery = _refundRepository.AsNoTracking()
            .Where(r => r.CreationTime >= startTime && r.CreationTime <= endTime && r.Status == RefundStatus.Succeeded);

        var refundCount = await refundQuery.CountAsync(cancellationToken);
        var totalRefunds = await refundQuery.SumAsync(r => (decimal?)r.RefundAmount, cancellationToken) ?? 0;
        var refundRate = totalTransactions > 0
            ? Math.Round((decimal)refundCount / totalTransactions * 100, 2)
            : 0;

        // 活跃订阅数
        var activeSubscriptions = await _subscriptionRepository.AsNoTracking()
            .CountAsync(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial, cancellationToken);

        // 渠道分布（数据库级 GroupBy）
        var channelDistribution = await paymentQuery
            .Where(p => p.Status == PaymentStatus.Succeeded)
            .GroupBy(p => p.ChannelCode)
            .Select(g => new Dtos.ChannelStatisticsDto
            {
                ChannelCode = g.Key,
                Revenue = g.Sum(p => p.PaidAmount),
                TransactionCount = g.Count()
            })
            .OrderByDescending(c => c.Revenue)
            .ToListAsync(cancellationToken);

        // 内存中计算占比（结果集有界，渠道数有限）
        foreach (var channel in channelDistribution)
        {
            channel.Percentage = totalRevenue > 0
                ? Math.Round(channel.Revenue / totalRevenue * 100, 2)
                : 0;
        }

        var result = new PaymentStatisticsDto
        {
            StartTime = startTime,
            EndTime = endTime,
            TotalRevenue = totalRevenue,
            TotalTransactions = totalTransactions,
            SuccessfulTransactions = successfulTransactions,
            FailedTransactions = failedTransactions,
            TotalRefunds = totalRefunds,
            RefundCount = refundCount,
            RefundRate = refundRate,
            ActiveSubscriptions = activeSubscriptions,
            ChannelDistribution = channelDistribution
        };

        return Ok(result);
    }

    public async Task<Result<List<RevenueTrendPointDto>>> GetRevenueTrendAsync(RevenueTrendQueryDto query, CancellationToken cancellationToken = default)
    {
        var endTime = query.EndTime ?? DateTime.UtcNow;
        var startTime = query.StartTime ?? endTime.AddDays(-30);

        // 数据库级 GroupBy 按天聚合（每日粒度）
        var dailyPayments = await _paymentRepository.AsNoTracking()
            .Where(p => p.CreationTime >= startTime && p.CreationTime <= endTime && p.Status == PaymentStatus.Succeeded)
            .GroupBy(p => p.CreationTime.Date)
            .Select(g => new
            {
                Date = g.Key,
                Revenue = g.Sum(p => p.PaidAmount),
                TransactionCount = g.Count()
            })
            .ToListAsync(cancellationToken);

        var dailyRefunds = await _refundRepository.AsNoTracking()
            .Where(r => r.CreationTime >= startTime && r.CreationTime <= endTime && r.Status == RefundStatus.Succeeded)
            .GroupBy(r => r.CreationTime.Date)
            .Select(g => new
            {
                Date = g.Key,
                RefundAmount = g.Sum(r => r.RefundAmount)
            })
            .ToListAsync(cancellationToken);

        var refundLookup = dailyRefunds.ToDictionary(r => r.Date, r => r.RefundAmount);

        // 构建每日数据点
        var dailyPoints = dailyPayments.Select(d =>
        {
            var refundAmount = refundLookup.GetValueOrDefault(d.Date);
            return new RevenueTrendPointDto
            {
                Date = d.Date,
                Revenue = d.Revenue,
                TransactionCount = d.TransactionCount,
                RefundAmount = refundAmount,
                NetRevenue = d.Revenue - refundAmount
            };
        }).ToList();

        // 按粒度重新聚合（内存中操作，结果集有界：最多 ~365 天）
        var result = query.Granularity switch
        {
            TrendGranularity.Week => AggregateByWeek(dailyPoints),
            TrendGranularity.Month => AggregateByMonth(dailyPoints),
            _ => dailyPoints.OrderBy(p => p.Date).ToList()
        };

        return Ok(result);
    }

    public async Task<Result<SubscriptionMetricsDto>> GetSubscriptionMetricsAsync(CancellationToken cancellationToken = default)
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

        // MRR：活跃订阅的计划价格折算为月度等值金额
        var mrrData = await _subscriptionRepository.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active && s.Plan != null)
            .Select(s => new { s.Plan!.Price, s.Plan.CycleType, s.Plan.CycleValue })
            .ToListAsync(cancellationToken);

        var mrr = mrrData.Sum(s => CalculateMonthlyEquivalent(s.Price, s.CycleType, s.CycleValue));

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

        var result = new SubscriptionMetricsDto
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

        return Ok(result);
    }

    /// <inheritdoc />
    public async Task<Result<ReconciliationExportResultDto>> ExportReconciliationAsync(ReconciliationQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var endTime = query.EndTime ?? DateTime.UtcNow;
        var startTime = query.StartTime ?? new DateTime(endTime.Year, endTime.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // 构建支付查询
        var paymentQuery = _paymentRepository.AsNoTracking()
            .Where(p => p.CreationTime >= startTime && p.CreationTime <= endTime);

        if (!string.IsNullOrWhiteSpace(query.ChannelCode))
        {
            paymentQuery = paymentQuery.Where(p => p.ChannelCode == query.ChannelCode);
        }

        if (query.Status.HasValue)
        {
            paymentQuery = paymentQuery.Where(p => p.Status == query.Status.Value);
        }

        var payments = await paymentQuery
            .OrderBy(p => p.CreationTime)
            .ToListAsync(cancellationToken);

        // 批量加载关联退款（已成功的退款）
        var paymentIds = payments.Select(p => p.Id).ToList();
        var refunds = await _refundRepository.AsNoTracking()
            .Where(r => paymentIds.Contains(r.PaymentId) && r.Status == RefundStatus.Succeeded)
            .GroupBy(r => r.PaymentId)
            .Select(g => new { PaymentId = g.Key, TotalRefund = g.Sum(r => r.RefundAmount) })
            .ToListAsync(cancellationToken);

        var refundLookup = refunds.ToDictionary(r => r.PaymentId, r => r.TotalRefund);

        // 构建对账行
        var entries = payments.Select(p =>
        {
            var refundAmount = refundLookup.GetValueOrDefault(p.Id);
            return new ReconciliationEntryDto
            {
                TradeNo = p.TradeNo,
                ExternalTradeNo = p.ExternalTradeNo,
                BusinessOrderNo = p.BusinessOrderNo,
                BusinessType = p.BusinessType.ToString(),
                ChannelCode = p.ChannelCode,
                PaymentMethod = p.PaymentMethod.ToString(),
                OriginalAmount = p.OriginalAmount,
                DiscountAmount = p.DiscountAmount,
                PaidAmount = p.PaidAmount,
                RefundAmount = refundAmount,
                NetAmount = p.PaidAmount - refundAmount,
                Currency = p.Currency,
                Status = p.Status.ToString(),
                CreationTime = p.CreationTime,
                PaidTime = p.PaidTime
            };
        }).ToList();

        // 生成 CSV
        var csv = new StringBuilder();
        csv.AppendLine("TradeNo,ExternalTradeNo,BusinessOrderNo,BusinessType,ChannelCode,PaymentMethod,OriginalAmount,DiscountAmount,PaidAmount,RefundAmount,NetAmount,Currency,Status,CreationTime,PaidTime");

        foreach (var entry in entries)
        {
            csv.AppendLine(string.Join(",",
                EscapeCsvField(entry.TradeNo),
                EscapeCsvField(entry.ExternalTradeNo ?? ""),
                EscapeCsvField(entry.BusinessOrderNo),
                EscapeCsvField(entry.BusinessType),
                EscapeCsvField(entry.ChannelCode),
                EscapeCsvField(entry.PaymentMethod),
                entry.OriginalAmount,
                entry.DiscountAmount,
                entry.PaidAmount,
                entry.RefundAmount,
                entry.NetAmount,
                EscapeCsvField(entry.Currency),
                EscapeCsvField(entry.Status),
                entry.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                entry.PaidTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""));
        }

        var totalRevenue = entries.Sum(e => e.PaidAmount);
        var totalRefunds = entries.Sum(e => e.RefundAmount);

        var result = new ReconciliationExportResultDto
        {
            CsvContent = csv.ToString(),
            FileName = $"reconciliation_{startTime:yyyyMMdd}_{endTime:yyyyMMdd}.csv",
            TotalRecords = entries.Count,
            TotalRevenue = totalRevenue,
            TotalRefunds = totalRefunds,
            NetRevenue = totalRevenue - totalRefunds
        };

        Logger.LogInformation("Reconciliation report exported: {TotalRecords} records, period {StartTime:yyyy-MM-dd} to {EndTime:yyyy-MM-dd}",
            entries.Count, startTime, endTime);

        return Ok(result);
    }

    /// <inheritdoc />
    public async Task<Result<List<PromotionAnalyticsDto>>> GetPromotionAnalyticsAsync(int topN = 10, DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        if (topN <= 0)
            return Fail<List<PromotionAnalyticsDto>>("topN must be greater than 0", 400);

        // 构建优惠券使用查询（含时间范围过滤）+ 数据库级 GroupBy
        var usageStats = await _couponUsageRepository.AsNoTracking()
            .Where(u => (!startDate.HasValue || u.CreationTime >= startDate.Value)
                && (!endDate.HasValue || u.CreationTime <= endDate.Value))
            .GroupBy(u => u.CouponId)
            .Select(g => new
            {
                PromotionId = g.Key,
                UsageCount = g.Count(),
                UniqueUsers = g.Select(u => u.UserId).Distinct().Count(),
                TotalDiscountAmount = g.Sum(u => u.DiscountAmount)
            })
            .OrderByDescending(s => s.UsageCount)
            .Take(topN)
            .ToListAsync(cancellationToken);

        if (usageStats.Count == 0)
            return Ok(new List<PromotionAnalyticsDto>());

        // 批量加载关联促销信息
        var promotionIds = usageStats.Select(s => s.PromotionId).ToList();
        var promotions = await _promotionRepository.AsNoTracking()
            .Where(p => promotionIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var promotionLookup = promotions.ToDictionary(p => p.Id);

        var result = usageStats.Select(s =>
        {
            var promotion = promotionLookup.GetValueOrDefault(s.PromotionId);
            return new PromotionAnalyticsDto
            {
                PromotionId = s.PromotionId,
                Name = promotion?.Name ?? string.Empty,
                PromotionCode = promotion?.PromotionCode ?? string.Empty,
                DiscountType = promotion?.DiscountType.ToString() ?? string.Empty,
                DiscountValue = promotion?.DiscountValue ?? 0,
                UsageCount = s.UsageCount,
                UniqueUsers = s.UniqueUsers,
                TotalDiscountAmount = s.TotalDiscountAmount,
                AverageDiscountPerUse = s.UsageCount > 0 ? Math.Round(s.TotalDiscountAmount / s.UsageCount, 2) : 0,
                RedemptionRate = promotion?.TotalUsageLimit > 0
                    ? Math.Round((decimal)(promotion.UsedCount) / promotion.TotalUsageLimit.Value * 100, 2)
                    : -1,
                IsActive = promotion?.IsActive ?? false
            };
        }).ToList();

        return Ok(result);
    }

    /// <inheritdoc />
    public async Task<Result<RefundAnalyticsDto>> GetRefundAnalyticsAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        var query = _refundRepository.AsNoTracking()
            .Where(r => (!startDate.HasValue || r.CreationTime >= startDate.Value)
                && (!endDate.HasValue || r.CreationTime <= endDate.Value));

        var refunds = await query.ToListAsync(cancellationToken);

        if (refunds.Count == 0)
        {
            return Ok(new RefundAnalyticsDto());
        }

        var totalCount = refunds.Count;
        var totalAmount = refunds.Sum(r => r.RefundAmount);

        // 平均处理时间（仅已完成的退款，CreationTime → CompletedTime）
        var completedRefunds = refunds.Where(r => r.CompletedTime.HasValue).ToList();
        var avgProcessingHours = completedRefunds.Count > 0
            ? completedRefunds.Average(r => (r.CompletedTime!.Value - r.CreationTime).TotalHours)
            : 0;

        // 退款原因分布
        var reasonBreakdown = refunds
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Reason) ? "Unspecified" : r.Reason)
            .Select(g => new RefundReasonBreakdownDto
            {
                Reason = g.Key,
                Count = g.Count(),
                Amount = g.Sum(r => r.RefundAmount),
                Percentage = Math.Round((decimal)g.Count() / totalCount * 100, 2)
            })
            .OrderByDescending(r => r.Count)
            .ToList();

        // 退款渠道分布（需要关联 Payment 的 ChannelCode）
        // 批量加载关联支付信息
        var paymentIds = refunds.Select(r => r.PaymentId).Distinct().ToList();
        var payments = await _paymentRepository.AsNoTracking()
            .Where(p => paymentIds.Contains(p.Id))
            .Select(p => new { p.Id, p.ChannelCode })
            .ToListAsync(cancellationToken);

        var paymentChannelLookup = payments.ToDictionary(p => p.Id, p => p.ChannelCode);

        var channelBreakdown = refunds
            .GroupBy(r => paymentChannelLookup.GetValueOrDefault(r.PaymentId, "Unknown"))
            .Select(g => new RefundChannelBreakdownDto
            {
                ChannelCode = g.Key,
                Count = g.Count(),
                Amount = g.Sum(r => r.RefundAmount),
                Percentage = Math.Round((decimal)g.Count() / totalCount * 100, 2)
            })
            .OrderByDescending(c => c.Count)
            .ToList();

        // 退款状态分布
        var statusBreakdown = refunds
            .GroupBy(r => r.Status)
            .Select(g => new RefundStatusBreakdownDto
            {
                Status = g.Key.ToString(),
                Count = g.Count(),
                Percentage = Math.Round((decimal)g.Count() / totalCount * 100, 2)
            })
            .OrderByDescending(s => s.Count)
            .ToList();

        var result = new RefundAnalyticsDto
        {
            TotalRefundCount = totalCount,
            TotalRefundAmount = totalAmount,
            AverageProcessingTimeHours = Math.Round(avgProcessingHours, 2),
            ReasonBreakdown = reasonBreakdown,
            ChannelBreakdown = channelBreakdown,
            StatusBreakdown = statusBreakdown
        };

        return Ok(result);
    }

    /// <summary>
    /// 转义 CSV 字段（处理逗号、引号、换行）
    /// </summary>
    private static string EscapeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
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

    private static List<RevenueTrendPointDto> AggregateByWeek(List<RevenueTrendPointDto> dailyPoints)
    {
        return dailyPoints
            .GroupBy(p => GetWeekStart(p.Date))
            .Select(g => new RevenueTrendPointDto
            {
                Date = g.Key,
                Revenue = g.Sum(p => p.Revenue),
                TransactionCount = g.Sum(p => p.TransactionCount),
                RefundAmount = g.Sum(p => p.RefundAmount),
                NetRevenue = g.Sum(p => p.NetRevenue)
            })
            .OrderBy(p => p.Date)
            .ToList();
    }

    private static List<RevenueTrendPointDto> AggregateByMonth(List<RevenueTrendPointDto> dailyPoints)
    {
        return dailyPoints
            .GroupBy(p => new DateTime(p.Date.Year, p.Date.Month, 1))
            .Select(g => new RevenueTrendPointDto
            {
                Date = g.Key,
                Revenue = g.Sum(p => p.Revenue),
                TransactionCount = g.Sum(p => p.TransactionCount),
                RefundAmount = g.Sum(p => p.RefundAmount),
                NetRevenue = g.Sum(p => p.NetRevenue)
            })
            .OrderBy(p => p.Date)
            .ToList();
    }

    private static DateTime GetWeekStart(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }
}
