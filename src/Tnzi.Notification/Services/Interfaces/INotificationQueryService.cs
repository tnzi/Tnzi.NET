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
}
