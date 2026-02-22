namespace Tnzi.Notification.Services;

/// <summary>
/// 用户通知收件箱服务接口
/// </summary>
public interface IUserNotificationService
{
    Task<Result<IPagedList<UserNotificationItem>>> GetInboxAsync(Guid userId, QueryUserNotificationRequest request, CancellationToken cancellationToken = default);
    Task<Result<UserNotificationDetail>> GetDetailAsync(Guid userId, Guid recipientId, CancellationToken cancellationToken = default);
    Task<Result> MarkAsReadAsync(Guid userId, Guid recipientId, CancellationToken cancellationToken = default);
    Task<Result> MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<UnreadCountDto>> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);
}
