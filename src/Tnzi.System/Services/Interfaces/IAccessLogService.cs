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
}
