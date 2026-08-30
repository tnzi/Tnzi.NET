namespace Tnzi.Payment.Services;

/// <summary>
/// 支付统计服务实现：支付与退款两块由本模块自己算，订阅那一块向
/// <see cref="IPaymentStatisticsContributor"/> 提问，促销那一块向
/// <see cref="IPromotionAnalyticsProvider"/> 提问。
/// </summary>
public class PaymentStatisticsService : ApplicationService, IPaymentStatisticsService
{
    /// <summary>未加载续费包时，订阅指标端点给出的答复。指名要加载哪个包，而不是只说"没实现"。</summary>
    private const string SubscriptionMetricsMissingMessage =
        "Subscription metrics require the recurring-billing package. "
        + "Load the Tnzi.Payment.Subscriptions module ([DependsOn(typeof(PaymentSubscriptionsModule))]) "
        + "or register your own IPaymentStatisticsContributor.";

    /// <summary>未加载促销包时，促销效果分析端点给出的答复。指名要加载哪个包，而不是只说"没实现"。</summary>
    private const string PromotionAnalyticsMissingMessage =
        "Promotion analytics require the discounting package. "
        + "Load the Tnzi.Payment.Promotions module ([DependsOn(typeof(PaymentPromotionsModule))]) "
        + "or register your own IPromotionAnalyticsProvider.";

    private readonly IRepository<PaymentEntity, Guid> _paymentRepository;
    private readonly IRepository<Refund, Guid> _refundRepository;

    /// <summary>
    /// 订阅那一半统计的供给方。未加载续费包时为 null：总览里的活跃订阅数变成 <c>null</c>
    /// （前端渲染"不适用"，而不是一个与"生意崩了"无法区分的 0），
    /// 整块订阅指标端点回 501。
    /// </summary>
    private readonly IPaymentStatisticsContributor? _subscriptionStatistics;

    /// <summary>
    /// 促销那一块统计的供给方。未加载促销包时为 null，此时促销效果分析端点回 501
    /// 并指名要加载哪个包 —— 而不是一个与"这段时间没人用券"无法区分的空列表。
    /// </summary>
    private readonly IPromotionAnalyticsProvider? _promotionAnalytics;

    public PaymentStatisticsService(
        IRepository<PaymentEntity, Guid> paymentRepository,
        IRepository<Refund, Guid> refundRepository,
        IServiceProvider serviceProvider,
        IPaymentStatisticsContributor? subscriptionStatistics = null,
        IPromotionAnalyticsProvider? promotionAnalytics = null)
        : base(serviceProvider)
    {
        _paymentRepository = Check.NotNull(paymentRepository);
        _refundRepository = Check.NotNull(refundRepository);
        _subscriptionStatistics = subscriptionStatistics;
        _promotionAnalytics = promotionAnalytics;
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

        // 活跃订阅数：向续费域提问。没有供给方时是 null 而**不是 0** ——
        // 「这台宿主不做订阅」和「所有订阅一夜之间全没了」不能在界面上长成同一个样子。
        int? activeSubscriptions = _subscriptionStatistics == null
            ? null
            : await _subscriptionStatistics.GetActiveSubscriptionCountAsync(cancellationToken);

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

    /// <summary>
    /// 整块订阅指标：全部由续费域计算。
    /// </summary>
    /// <remarks>
    /// 这个端点<b>整个</b>只讲订阅，没有任何一半是本模块的，所以未加载续费包时
    /// 501「本服务器不提供此功能」才是准确答复，并在文案里指名要加载哪个包。
    /// <b>不是 503</b>：503 意味着暂时故障，会让监控和客户端不停重试一件永远不会恢复的事。
    /// 与之相对，总览端点 <see cref="GetStatisticsAsync"/> 的支付与退款那一半是本模块自己的，
    /// 因此它照常 200，只把活跃订阅数留空。
    /// </remarks>
    public async Task<Result<SubscriptionMetricsDto>> GetSubscriptionMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (_subscriptionStatistics == null)
            return Fail<SubscriptionMetricsDto>(SubscriptionMetricsMissingMessage, 501);

        var metrics = await _subscriptionStatistics.GetSubscriptionMetricsAsync(cancellationToken);

        // 供给方注册了却给不出答案：仍然是"本服务器不提供此功能"，不是一份全零的报表。
        return metrics == null
            ? Fail<SubscriptionMetricsDto>(SubscriptionMetricsMissingMessage, 501)
            : Ok(metrics);
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

        // 生成 CSV(单元格转义统一走核心 CsvBuilder,含公式注入防护;金额 invariant culture 输出)
        var csv = new CsvBuilder("yyyy-MM-dd HH:mm:ss");
        csv.AppendRow("TradeNo", "ExternalTradeNo", "BusinessOrderNo", "BusinessType", "ChannelCode", "PaymentMethod", "OriginalAmount", "DiscountAmount", "PaidAmount", "RefundAmount", "NetAmount", "Currency", "Status", "CreationTime", "PaidTime");

        foreach (var entry in entries)
        {
            csv.AppendRow(entry.TradeNo, entry.ExternalTradeNo, entry.BusinessOrderNo,
                entry.BusinessType, entry.ChannelCode, entry.PaymentMethod,
                entry.OriginalAmount, entry.DiscountAmount, entry.PaidAmount,
                entry.RefundAmount, entry.NetAmount, entry.Currency, entry.Status,
                entry.CreationTime, entry.PaidTime);
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
    /// <remarks>
    /// 这个端点<b>整个</b>只讲促销，因此缺席时回 501（本服务器不提供此功能）并指名要加载哪个包。
    /// 不是空列表：空列表是一个答案（"有促销，只是这段时间没人用"），会把一次部署疏漏
    /// 伪装成一条业务结论。也不是 503：503 意味着暂时故障，会让监控和客户端不停重试
    /// 一件永远不会恢复的事。
    /// <c>topN</c> 的入参校验留在这里而不是下放给供给方 —— 一个非法请求是 400，
    /// 这一点不该随「装没装促销包」而变。
    /// </remarks>
    public async Task<Result<List<PromotionAnalyticsDto>>> GetPromotionAnalyticsAsync(int topN = 10, DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        if (topN <= 0)
            return Fail<List<PromotionAnalyticsDto>>("topN must be greater than 0", 400);

        var analytics = _promotionAnalytics == null
            ? null
            : await _promotionAnalytics.GetTopPromotionsAsync(topN, startDate, endDate, cancellationToken);

        if (analytics == null)
        {
            Logger.LogWarning("Promotion analytics refused. {Guidance}", PromotionAnalyticsMissingMessage);
            return Fail<List<PromotionAnalyticsDto>>(PromotionAnalyticsMissingMessage, 501);
        }

        return Ok(analytics);
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
