namespace Tnzi.Payment.Controllers.Admin;

/// <summary>
/// 支付统计管理控制器基类
/// </summary>
[Route("admin/payment-statistics")]
[DefaultController]
[ApiAuthorize(PermissionName = "payment.statistics.view")]
public class DefaultPaymentStatisticsAdminController : ApiAdminControllerBase
{
    private readonly IPaymentStatisticsService _statisticsService;

    public DefaultPaymentStatisticsAdminController(IPaymentStatisticsService statisticsService)
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
    /// <remarks>
    /// ★ 文件之外，响应头带着这次导出的行数与是否截断（<see cref="PaymentConstants.ReconciliationTruncatedHeader"/> 等三个）。
    /// 下载没有 JSON 信封，此前截断只记在服务端日志里：超过单次上限的那个月下载下来看起来完整，少掉的行被读成对账差异。
    /// 先调摘要端点同样能知道会不会截断。
    /// </remarks>
    [HttpGet("reconciliation/export")]
    public virtual async Task<IActionResult> ExportReconciliation([FromQuery] ReconciliationQueryDto query)
    {
        var result = await _statisticsService.ExportReconciliationAsync(query);
        if (!result.Succeeded)
        {
            // 按 Result 状态码透传标准信封,前端 download 客户端解析错误 body 提取 message
            return StatusCode(result.Code ?? 400, result.ToApiResult());
        }

        var data = result.Data!;
        Response.Headers[PaymentConstants.ReconciliationTotalRecordsHeader] = data.TotalRecords.ToString(CultureInfo.InvariantCulture);
        Response.Headers[PaymentConstants.ReconciliationMatchedRecordsHeader] = data.MatchedRecords.ToString(CultureInfo.InvariantCulture);
        Response.Headers[PaymentConstants.ReconciliationTruncatedHeader] = data.Truncated ? "true" : "false";

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(data.CsvContent ?? string.Empty)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", data.FileName);
    }

    /// <summary>
    /// 获取对账报表摘要（不下载文件）：行数、是否截断与汇总金额，不带 CSV 本身（<c>CsvContent</c> 为 null）。
    /// </summary>
    /// <remarks>
    /// 此前摘要把整份 CSV（上限 5 万行）原样塞进 JSON 返回，「先看一眼会不会截断」就等于先下载一遍。
    /// </remarks>
    [HttpGet("reconciliation/summary")]
    public virtual async Task<ApiResult<ReconciliationExportResultDto>> GetReconciliationSummary([FromQuery] ReconciliationQueryDto query)
    {
        var result = await _statisticsService.ExportReconciliationAsync(query);
        if (result.Succeeded && result.Data != null)
            result.Data.CsvContent = null;

        return result.ToApiResult();
    }
}
