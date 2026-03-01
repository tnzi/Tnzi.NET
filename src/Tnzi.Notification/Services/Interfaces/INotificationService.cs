using Message = Tnzi.Notification.Entities.Message;

namespace Tnzi.Notification.Services;

/// <summary>
/// 通知服务接口（创建+发送编排）
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// 创建通知（不发送）
    /// </summary>
    Task<Result<NotificationInfo>> CreateAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建并发送通知
    /// </summary>
    Task<Result<NotificationInfo>> CreateAndSendAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量创建并发送通知
    /// </summary>
    Task<Result<IEnumerable<NotificationInfo>>> CreateManyAndSendAsync(IEnumerable<CreateNotificationRequest> requests, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送通知
    /// </summary>
    Task<Result> SendAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消通知
    /// </summary>
    Task<Result> CancelAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量取消通知 (pending/scheduled 状态)
    /// </summary>
    Task<Result<int>> BatchCancelAsync(List<Guid> ids, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<int>("Batch cancel not implemented", 501));
    }
}
