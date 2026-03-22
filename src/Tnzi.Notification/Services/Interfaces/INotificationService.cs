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

    /// <summary>
    /// 预览通知渲染结果（不创建、不发送）
    /// </summary>
    Task<Result<NotificationPreviewDto>> PreviewAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<NotificationPreviewDto>("Preview not implemented", 501));
    }

    /// <summary>
    /// 重发消息给失败的接收人
    /// </summary>
    Task<Result<int>> ResendToFailedRecipientsAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<int>("Resend to failed not implemented", 501));
    }
}
