namespace Tnzi.Notification.Services;

/// <summary>
/// 通知重试服务接口
/// </summary>
public interface INotificationRetryService
{
    /// <summary>
    /// 重试失败的通知
    /// </summary>
    Task<Result> RetryAsync(Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量重试失败的通知
    /// </summary>
    Task<Result> RetryFailedAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default);
}
