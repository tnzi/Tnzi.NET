namespace Tnzi.System.Services;

/// <summary>
/// 访问日志服务接口
/// </summary>
public interface IAccessLogService
{
    /// <summary>
    /// 记录访问日志
    /// </summary>
    Task<Result> LogAccessAsync(AccessLogDto log);

    /// <summary>
    /// 获取访问日志（分页）
    /// </summary>
    Task<Result<IPagedList<AccessLogDto>>> GetAccessLogsAsync(AccessLogQueryDto query);

    /// <summary>
    /// 根据ID获取访问日志
    /// </summary>
    Task<Result<AccessLogDto>> GetAccessLogByIdAsync(Guid id);

    /// <summary>
    /// 获取访问日志统计
    /// </summary>
    Task<Result<AccessLogStatisticsDto>> GetAccessLogStatisticsAsync(DateTime? startTime = null, DateTime? endTime = null);

    /// <summary>
    /// 删除过期访问日志
    /// </summary>
    Task<Result> DeleteExpiredAccessLogsAsync(int days = 90);

    /// <summary>
    /// 批量删除访问日志
    /// </summary>
    Task<Result> DeleteAccessLogsAsync(IEnumerable<Guid> ids);

    /// <summary>
    /// 获取访问日志趋势统计（按天/周/月）
    /// </summary>
    Task<Result<AccessLogTrendDto>> GetAccessLogTrendAsync(
        TrendInterval interval,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<AccessLogTrendDto>("Access log trend not implemented", 501));
    }

    /// <summary>
    /// 获取 Top 端点统计（最热/最慢/最多错误）
    /// </summary>
    Task<Result<List<TopEndpointDto>>> GetTopEndpointsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        int top = 10,
        TopEndpointSortBy sortBy = TopEndpointSortBy.Hits,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<List<TopEndpointDto>>("Top endpoints not implemented", 501));
    }
}
