namespace Tnzi.Notification.Services;

/// <summary>
/// 通知查询服务接口（管理端查询+统计）
/// </summary>
public interface INotificationQueryService
{
    Task<Result<IPagedList<NotificationInfo>>> QueryAsync(QueryNotificationRequest request, CancellationToken cancellationToken = default);
    Task<Result<NotificationInfo>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<NotificationStatisticsDto>> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<ChannelStatisticsDto>>> GetStatisticsByChannelAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<StatusStatisticsDto>>> GetStatisticsByStatusAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<NotificationInfo>>> GetFailedNotificationsAsync(DateTime? startDate = null, DateTime? endDate = null, int top = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询计划中的通知 (scheduled 状态)
    /// </summary>
    Task<Result<IPagedList<NotificationInfo>>> GetScheduledAsync(QueryNotificationRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<IPagedList<NotificationInfo>>("Get scheduled not implemented", 501));
    }

    /// <summary>
    /// 批量删除通知 (soft delete via FullAuditedEntity)
    /// </summary>
    Task<Result<int>> BatchDeleteAsync(List<Guid> ids, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<int>("Batch delete not implemented", 501));
    }

    /// <summary>
    /// 获取通知统计趋势 (按天/周/月)
    /// </summary>
    Task<Result<NotificationTrendDto>> GetStatisticsTrendAsync(TrendInterval interval, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<NotificationTrendDto>("Statistics trend not implemented", 501));
    }
}
