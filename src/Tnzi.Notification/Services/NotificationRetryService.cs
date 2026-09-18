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
    private readonly IOptionsMonitor<NotificationOptions> _optionsMonitor;

    private NotificationOptions Options => _optionsMonitor.CurrentValue;

    public NotificationRetryService(
        IRepository<Message, Guid> notificationRepository,
        INotificationService notificationService,
        IOptionsMonitor<NotificationOptions> optionsMonitor,
        IServiceProvider serviceProvider,
        INotificationQueueService? queueService = null)
        : base(serviceProvider)
    {
        _notificationRepository = Check.NotNull(notificationRepository);
        _notificationService = Check.NotNull(notificationService);
        _optionsMonitor = Check.NotNull(optionsMonitor);
        _queueService = queueService;
    }

    public async Task<Result> RetryAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        // ★★ 必须带跟踪加载。此前走的是默认的 AsNoTracking：下面改的是**子实体**
        // （每个 Recipient 的 Status / FailureReason），而 UpdateAsync 只把根实体
        // 置为 Modified，整个图是 Attach 进来的 Unchanged —— 收件人那些改动一个都不落库。
        // 症状是「重试报告成功，而库里那些人仍然是 Failed」，随后 SendAsync 照样会挑到
        // 它们（它认 Pending/Failed），所以看起来还工作，只是 RetryCount 与状态机对不上。
        var notification = await _notificationRepository
            .AsQueryable(withTracking: true)
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

        // 通过队列调度重试（带退避延迟）
        var delaySeconds = CalculateRetryDelay(notification.RetryCount);
        if (_queueService != null)
        {
            var delay = TimeSpan.FromSeconds(delaySeconds);
            LogInformation("Retrying notification {NotificationId} via queue with {DelaySeconds}s delay (attempt {RetryCount}/{MaxRetryCount})",
                messageId, delaySeconds, notification.RetryCount + 1, notification.MaxRetryCount);
            // 租户直接取自这一行：它是在当前租户的过滤器下加载出来的，带着它进队列，
            // 延迟到期后的那个新作用域才查得到它（见 NotificationWorkItem）。
            await _queueService.EnqueueWithDelayAsync(
                NotificationWorkItem.SendMessage(messageId, notification.TenantId), delay);
        }
        else
        {
            var sendResult = await _notificationService.SendAsync(messageId, cancellationToken);
            if (!sendResult.Succeeded)
                return sendResult;
        }

        return Ok("Notification retry initiated");
    }

    /// <summary>
    /// 批量重试失败的消息。<b>一次最多接手 <see cref="DispatchOptions.RecoveryBatchSize"/> 条</b>。
    /// </summary>
    /// <remarks>
    /// ★★ <b>闸门不是性能优化。</b>每条 <see cref="RetryAsync"/> 各发一次带 Include 的查询、
    /// 各入一次队；不设上限时一次点击就能把整张历史失败表灌进发送管线 —— 而这是本模块
    /// 唯一一个「一次操作触发无上界发送量」的入口。恢复扫描早就有这道闸门
    /// （<c>RecoveryBatchSize</c>），这条一直没有，两者面对的是同一件事（一批要重发的消息），
    /// 所以共用同一个上限而不是新开一个配置项。
    /// <para>
    /// ★ 上限与<b>实际总数</b>一起报回去，并按最早创建的先处理：截断如果不说出来，
    /// 管理员会以为剩下的已经处理完了，而只有「再点一次」才能推进 —— 无声的截断
    /// 与「全部处理完了」在界面上长得一模一样。
    /// </para>
    /// </remarks>
    public async Task<Result> RetryFailedAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        var query = _notificationRepository
            .AsQueryable()
            .Where(n => n.Status == NotificationStatus.Failed && n.RetryCount < n.MaxRetryCount);

        if (startDate.HasValue)
            query = query.Where(n => n.CreationTime >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(n => n.CreationTime <= endDate.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        if (totalCount == 0)
            return Ok("No failed notifications to retry");

        var batchSize = Math.Max(1, Options.Dispatch.RecoveryBatchSize);

        var failedIds = await query
            .OrderBy(n => n.CreationTime)
            .Take(batchSize)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

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

        var remaining = totalCount - failedIds.Count;
        var message = $"Retry batch completed: {successCount} initiated, {errorCount} failed";
        if (remaining > 0)
            message += $". {remaining} more failed notification(s) were left for a following run (batch limit {batchSize}).";

        return Ok(message);
    }

    private int CalculateRetryDelay(int retryCount)
    {
        var delaySeconds = Options.Retry.RetryDelaySeconds;
        if (Options.Retry.EnableExponentialBackoff)
            delaySeconds = (int)(delaySeconds * Math.Pow(2, retryCount));

        const int maxDelaySeconds = 86400;
        if (delaySeconds > maxDelaySeconds)
            delaySeconds = maxDelaySeconds;
        if (delaySeconds < 0)
            delaySeconds = 0;

        return delaySeconds;
    }
}
