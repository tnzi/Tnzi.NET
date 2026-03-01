namespace Tnzi.Payment.Controllers.Admin;

/// <summary>
/// 支付统计管理控制器基类
/// </summary>
[ApiExplorerSettings(GroupName = "admin")]
[Route("admin/payment-statistics")]
public abstract class PaymentStatisticsAdminControllerBase : ApiAdminControllerBase
{
    private readonly IPaymentStatisticsService _statisticsService;

    protected PaymentStatisticsAdminControllerBase(IPaymentStatisticsService statisticsService)
    {
        _statisticsService = Check.NotNull(statisticsService);
    }

    /// <summary>
    /// 获取支付统计概览
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<PaymentStatisticsDto>> GetStatistics([FromQuery] StatisticsQueryDto query)
    {
        var result = await _statisticsService.GetStatisticsAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取收入趋势
    /// </summary>
    [HttpGet("revenue-trend")]
    public virtual async Task<ApiResult<List<RevenueTrendPointDto>>> GetRevenueTrend([FromQuery] RevenueTrendQueryDto query)
    {
        var result = await _statisticsService.GetRevenueTrendAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取订阅指标
    /// </summary>
    [HttpGet("subscription-metrics")]
    public virtual async Task<ApiResult<SubscriptionMetricsDto>> GetSubscriptionMetrics()
    {
        var result = await _statisticsService.GetSubscriptionMetricsAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取促销效果分析
    /// </summary>
    [HttpGet("promotion-analytics")]
    public virtual async Task<ApiResult<List<PromotionAnalyticsDto>>> GetPromotionAnalytics(
        [FromQuery] int topN = 10, [FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
    {
        var result = await _statisticsService.GetPromotionAnalyticsAsync(topN, startDate, endDate);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取退款分析
    /// </summary>
    [HttpGet("refund-analytics")]
    public virtual async Task<ApiResult<RefundAnalyticsDto>> GetRefundAnalytics(
        [FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
    {
        var result = await _statisticsService.GetRefundAnalyticsAsync(startDate, endDate);
        return result.ToApiResult();
    }

    /// <summary>
    /// 导出对账报表（CSV）
    /// </summary>
    [HttpGet("reconciliation/export")]
    public virtual async Task<IActionResult> ExportReconciliation([FromQuery] ReconciliationQueryDto query)
    {
        var result = await _statisticsService.ExportReconciliationAsync(query);
        if (!result.Succeeded)
        {
            return BadRequest(result.ToApiResult());
        }

        var data = result.Data!;
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(data.CsvContent)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", data.FileName);
    }

    /// <summary>
    /// 获取对账报表摘要（不下载文件）
    /// </summary>
    [HttpGet("reconciliation/summary")]
    public virtual async Task<ApiResult<ReconciliationExportResultDto>> GetReconciliationSummary([FromQuery] ReconciliationQueryDto query)
    {
        var result = await _statisticsService.ExportReconciliationAsync(query);
        return result.ToApiResult();
    }
}
