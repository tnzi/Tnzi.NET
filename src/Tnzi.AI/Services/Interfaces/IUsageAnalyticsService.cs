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
}
