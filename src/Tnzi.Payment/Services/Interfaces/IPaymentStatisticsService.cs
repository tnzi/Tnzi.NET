namespace Tnzi.Payment.Services;

/// <summary>
/// 支付统计服务接口
/// </summary>
public interface IPaymentStatisticsService
{
    /// <summary>
    /// 获取支付统计概览
    /// </summary>
    Task<Result<PaymentStatisticsDto>> GetStatisticsAsync(StatisticsQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取收入趋势（按天/周/月分组）
    /// </summary>
    Task<Result<List<RevenueTrendPointDto>>> GetRevenueTrendAsync(RevenueTrendQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取订阅指标（MRR、流失率、ARPU 等）
    /// </summary>
    Task<Result<SubscriptionMetricsDto>> GetSubscriptionMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 导出对账报表（CSV 格式）
    /// </summary>
    Task<Result<ReconciliationExportResultDto>> ExportReconciliationAsync(ReconciliationQueryDto query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<ReconciliationExportResultDto>("Reconciliation export not implemented", 501));
    }

    /// <summary>
    /// 获取促销效果分析（Top N 促销按使用量排名，含唯一用户数、总折扣、兑换率）
    /// </summary>
    Task<Result<List<PromotionAnalyticsDto>>> GetPromotionAnalyticsAsync(int topN = 10, DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<List<PromotionAnalyticsDto>>("GetPromotionAnalyticsAsync not implemented", 501));
    }

    /// <summary>
    /// 获取退款分析（退款原因分布、渠道分布、平均处理时长、状态分布）
    /// </summary>
    Task<Result<RefundAnalyticsDto>> GetRefundAnalyticsAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<RefundAnalyticsDto>("GetRefundAnalyticsAsync not implemented", 501));
    }
}
