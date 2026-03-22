namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 使用量分析服务接口 — 提供聚合查询和统计
/// </summary>
public interface IUsageAnalyticsService
{
    /// <summary>
    /// 获取使用量统计摘要
    /// </summary>
    Task<Result<UsageSummaryDto>> GetSummaryAsync(UsageSummaryQueryDto query);

    /// <summary>
    /// 获取使用日志列表（分页）
    /// </summary>
    Task<Result<IPagedList<UsageLogDto>>> GetLogsAsync(UsageLogQueryDto query);

    /// <summary>
    /// 按提供商分组统计 Token 使用量
    /// </summary>
    Task<Result<List<ProviderUsageDto>>> GetUsageByProviderAsync(DateTime startTime, DateTime endTime);

    /// <summary>
    /// 按模型分组统计 Token 使用量
    /// </summary>
    Task<Result<List<ModelUsageDto>>> GetUsageByModelAsync(DateTime startTime, DateTime endTime, string? provider = null);

    /// <summary>
    /// 按时间粒度统计使用量趋势（Daily/Weekly/Monthly）
    /// </summary>
    Task<Result<List<UsageTrendPointDto>>> GetUsageTrendAsync(UsageTrendQueryDto query);

    /// <summary>
    /// 按 Agent 分组统计 Token 使用量，返回 Top N 消耗 Agent
    /// </summary>
    Task<Result<List<AgentUsageDto>>> GetUsageByAgentAsync(DateTime startTime, DateTime endTime, int topN = 20);

    /// <summary>
    /// 获取 Agent 反馈统计（按好评率降序）
    /// </summary>
    Task<Result<List<AgentFeedbackStatsDto>>> GetAgentFeedbackStatsAsync(int topN = 20);

    /// <summary>
    /// 获取成本摘要（按提供商和模型分组的成本统计）
    /// </summary>
    Task<Result<CostSummaryDto>> GetCostSummaryAsync(DateTime startTime, DateTime endTime, string? provider = null, string? model = null);
}
