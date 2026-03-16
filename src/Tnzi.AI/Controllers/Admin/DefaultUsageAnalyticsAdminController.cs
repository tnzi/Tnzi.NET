namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// 使用量分析管理控制器
/// 提供使用日志查询和统计分析端点
/// </summary>
[DefaultController]
[Route("admin/ai/usage")]
public class DefaultUsageAnalyticsAdminController : ApiAdminControllerBase
{
    protected readonly IUsageAnalyticsService AnalyticsService;

    /// <summary>
    /// 初始化使用量分析管理控制器
    /// </summary>
    public DefaultUsageAnalyticsAdminController(IUsageAnalyticsService analyticsService)
    {
        AnalyticsService = Check.NotNull(analyticsService);
    }

    /// <summary>
    /// 获取使用量统计摘要
    /// </summary>
    [HttpPost("summary")]
    public virtual async Task<ApiResult<UsageSummaryDto>> GetSummary([FromBody] UsageSummaryQueryDto query)
    {
        var result = await AnalyticsService.GetSummaryAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取使用日志列表（分页）
    /// </summary>
    [HttpPost("logs")]
    public virtual async Task<ApiResult<IPagedList<UsageLogDto>>> GetLogs([FromBody] UsageLogQueryDto query)
    {
        var result = await AnalyticsService.GetLogsAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 按提供商分组统计
    /// </summary>
    [HttpGet("by-provider")]
    public virtual async Task<ApiResult<List<ProviderUsageDto>>> GetByProvider([FromQuery] DateTime startTime, [FromQuery] DateTime endTime)
    {
        var result = await AnalyticsService.GetUsageByProviderAsync(startTime, endTime);
        return result.ToApiResult();
    }

    /// <summary>
    /// 按模型分组统计
    /// </summary>
    [HttpGet("by-model")]
    public virtual async Task<ApiResult<List<ModelUsageDto>>> GetByModel([FromQuery] DateTime startTime, [FromQuery] DateTime endTime, [FromQuery] string? provider = null)
    {
        var result = await AnalyticsService.GetUsageByModelAsync(startTime, endTime, provider);
        return result.ToApiResult();
    }

    /// <summary>
    /// 使用量趋势统计（按日/周/月聚合时序数据）
    /// </summary>
    [HttpPost("trend")]
    public virtual async Task<ApiResult<List<UsageTrendPointDto>>> GetTrend([FromBody] UsageTrendQueryDto query)
    {
        var result = await AnalyticsService.GetUsageTrendAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 按 Agent 分组统计（Top N 消耗 Agent）
    /// </summary>
    [HttpGet("by-agent")]
    public virtual async Task<ApiResult<List<AgentUsageDto>>> GetByAgent([FromQuery] DateTime startTime, [FromQuery] DateTime endTime, [FromQuery] int topN = 20)
    {
        var result = await AnalyticsService.GetUsageByAgentAsync(startTime, endTime, topN);
        return result.ToApiResult();
    }
}
