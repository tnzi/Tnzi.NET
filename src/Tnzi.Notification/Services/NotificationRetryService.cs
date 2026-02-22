using Message = Tnzi.Notification.Entities.Message;

namespace Tnzi.Notification.Services;

/// <summary>
/// 通知重试服务实现
/// </summary>
public class NotificationRetryService : ApplicationService, INotificationRetryService
{
    private readonly IRepository<Message, Guid> _notificationRepository;
    private readonly INotificationService _notificationService;
    private readonly INotificationQueueService? _queueService;
    private readonly NotificationOptions _options;

    public NotificationRetryService(
        IRepository<Message, Guid> notificationRepository,
        INotificationService notificationService,
        IOptions<NotificationOptions> options,
        IServiceProvider serviceProvider,
        INotificationQueueService? queueService = null)
        : base(serviceProvider)
    {
        _notificationRepository = Check.NotNull(notificationRepository);
        _notificationService = Check.NotNull(notificationService);
        _options = Check.NotNull(options).Value;
        _queueService = queueService;
    }

    public async Task<Result> RetryAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository
            .AsQueryable()
            .Include(n => n.Recipients)
            .Include(n => n.Attachments)
            .FirstOrDefaultAsync(n => n.Id == messageId, cancellationToken);

        if (notification == null)
            return Fail($"Notification {messageId} not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (notification.Status != NotificationStatus.Failed)
            return Fail($"Notification {messageId} is not in failed status", 400, ErrorCodes.NOTIFICATION_ERROR);

        if (notification.RetryCount >= notification.MaxRetryCount)
            return Fail($"Notification {messageId} has reached max retry count ({notification.MaxRetryCount})", 400, ErrorCodes.NOTIFICATION_ERROR);

        if (notification.Recipients == null)
            return Fail($"Notification {messageId} has null Recipients list, cannot retry", 400, ErrorCodes.NOTIFICATION_ERROR);

        // 重置失败状态
        notification.Status = NotificationStatus.Pending;
        notification.FailureReason = null;

        var failedRecipients = notification.Recipients.Where(r => r.Status == NotificationStatus.Failed).ToList();
        var cancelledRecipientCount = notification.Recipients.Count(r => r.Status == NotificationStatus.Cancelled);

        foreach (var recipient in failedRecipients)
        {
            recipient.Status = NotificationStatus.Pending;
            recipient.FailureReason = null;
        }

        if (cancelledRecipientCount > 0)
        {
            Logger.LogInformation(
                "Retrying notification {NotificationId}: {FailedCount} failed recipients will be retried, {CancelledCount} cancelled recipients will be skipped",
                messageId, failedRecipients.Count, cancelledRecipientCount);
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);

        // 通过队列调度重试（避免 Task.Delay 阻塞线程）
        var delaySeconds = CalculateRetryDelay(notification.RetryCount);
        if (_queueService != null && delaySeconds > 0)
        {
            LogInformation("Retrying notification {NotificationId} via queue with {DelaySeconds}s delay (attempt {RetryCount}/{MaxRetryCount})",
                messageId, delaySeconds, notification.RetryCount + 1, notification.MaxRetryCount);
            await _queueService.EnqueueAsync((sp, ct) =>
            {
                var svc = sp.GetRequiredService<INotificationService>();
                return svc.SendAsync(messageId, ct);
            });
        }
        else
        {
            var sendResult = await _notificationService.SendAsync(messageId, cancellationToken);
            if (!sendResult.Succeeded)
                return sendResult;
        }

        return Ok("Notification retry initiated");
    }

    public async Task<Result> RetryFailedAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        var query = _notificationRepository
            .AsQueryable()
            .Where(n => n.Status == NotificationStatus.Failed && n.RetryCount < n.MaxRetryCount);

        if (startDate.HasValue)
            query = query.Where(n => n.CreationTime >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(n => n.CreationTime <= endDate.Value);

        var failedIds = await query
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

        if (failedIds.Count == 0)
            return Ok("No failed notifications to retry");

        var successCount = 0;
        var errorCount = 0;

        foreach (var id in failedIds)
        {
            var result = await RetryAsync(id, cancellationToken);
            if (result.Succeeded)
                successCount++;
            else
                errorCount++;
        }

        return Ok($"Retry batch completed: {successCount} initiated, {errorCount} failed");
    }

    private int CalculateRetryDelay(int retryCount)
    {
        var delaySeconds = _options.Retry.RetryDelaySeconds;
        if (_options.Retry.EnableExponentialBackoff)
            delaySeconds = (int)(delaySeconds * Math.Pow(2, retryCount));

        const int maxDelaySeconds = 86400;
        if (delaySeconds > maxDelaySeconds)
            delaySeconds = maxDelaySeconds;
        if (delaySeconds < 0)
            delaySeconds = 0;

        return delaySeconds;
    }
}
