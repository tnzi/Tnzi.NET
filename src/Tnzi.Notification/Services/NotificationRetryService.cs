using Message = Tnzi.Notification.Entities.Message;

namespace Tnzi.Notification.Services;

/// <summary>
/// 通知重试服务实现
/// </summary>
public class NotificationRetryService : ApplicationService, INotificationRetryService
{
    private readonly IRepository<Message, Guid> _notificationRepository;
    private readonly IRepository<Recipient, Guid> _recipientRepository;
    private readonly INotificationService _notificationService;
    private readonly INotificationQueueService? _queueService;
    private readonly IOptionsMonitor<NotificationOptions> _optionsMonitor;

    private NotificationOptions Options => _optionsMonitor.CurrentValue;

    public NotificationRetryService(
        IRepository<Message, Guid> notificationRepository,
        IRepository<Recipient, Guid> recipientRepository,
        INotificationService notificationService,
        IOptionsMonitor<NotificationOptions> optionsMonitor,
        IServiceProvider serviceProvider,
        INotificationQueueService? queueService = null)
        : base(serviceProvider)
    {
        _notificationRepository = Check.NotNull(notificationRepository);
        // 重试把失败的收件人改回待发是一条按状态带条件的批量更新（见 RetryAsync），要直接对收件人表下手。
        _recipientRepository = Check.NotNull(recipientRepository);
        _notificationService = Check.NotNull(notificationService);
        _optionsMonitor = Check.NotNull(optionsMonitor);
        _queueService = queueService;
    }

    /// <remarks>
    /// <para>
    /// ★★ <b>重新武装是两条条件更新，不是读改写。</b>消息从 <c>Failed</c> 改回 <c>Pending</c>（条件里带着读到的
    /// <c>RetryCount</c>），成功之后再把仍是 <c>Failed</c> 的收件人改回 <c>Pending</c>。此前这里把消息连同收件人读进来、
    /// 改完再整行写回：读与写之间「重发失败项」认领了这条消息并送达了几个人，整行写回把消息改回 <c>Pending</c>、
    /// 把刚记成 <c>Sent</c> 的收件人写回 <c>Pending</c>，这次重试于是把他们再发一遍；同时点两次「重试」则各加一次的
    /// <c>RetryCount</c> 互相覆盖、各入一次队。现在读完之后状态变了就答 409，什么都不写。
    /// </para>
    /// <para>
    /// 收件人的改写只碰 <c>Failed</c> 的那些，所以子实体不需要被跟踪：这里一个实体都不加载进变更跟踪器，
    /// 随后（没有队列时）当场发送的 <c>SendAsync</c> 读到的就是库里的新状态。
    /// </para>
    /// </remarks>
    public async Task<Result> RetryAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        // 不跟踪、不带收件人：判断只用消息行本身，写回走下面的条件更新。
        var notification = await _notificationRepository.AsQueryable()
            .FirstOrDefaultAsync(n => n.Id == messageId, cancellationToken);

        if (notification == null)
            return Fail($"Notification {messageId} not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (notification.Status != NotificationStatus.Failed)
            return Fail($"Notification {messageId} is not in failed status", 400, ErrorCodes.NOTIFICATION_ERROR);

        if (notification.RetryCount >= notification.MaxRetryCount)
            return Fail($"Notification {messageId} has reached max retry count ({notification.MaxRetryCount})", 400, ErrorCodes.NOTIFICATION_ERROR);

        var delaySeconds = CalculateRetryDelay(notification.RetryCount);
        var observedRetryCount = notification.RetryCount;
        var now = DateTime.UtcNow;

        // ★★ 「什么时候再发」必须落在行上：退避是进程内的 Task.Delay，进程在它到期之前退出，
        // 这一次重试就没人记得了：行停在 Pending，「重试失败项」只挑 Failed，管理端只对失败的行给「重发」。
        // Pending + ScheduledTime 是派发恢复认得出的「已经交给发送」（见 NotificationService.QueuedAt），
        // 超过宽限仍停在 Pending 就由它接手。
        var dueTime = now.AddSeconds(delaySeconds);

        // 重试次数只在这里计：发送路径被走了几遍（首发、续发、延后到点）不算重试。
        var rearmed = await _notificationRepository.AsQueryable()
            .Where(n => n.Id == messageId && n.Status == NotificationStatus.Failed && n.RetryCount == observedRetryCount)
            .ExecuteUpdateAsync(
                s => s.SetProperty(n => n.Status, NotificationStatus.Pending)
                      .SetProperty(n => n.FailureReason, (string?)null)
                      .SetProperty(n => n.ScheduledTime, dueTime)
                      .SetProperty(n => n.RetryCount, n => n.RetryCount + 1)
                      .SetProperty(n => n.LastModificationTime, now),
                cancellationToken);
        if (rearmed == 0)
            return Fail($"Notification {messageId} changed state while it was being retried; reload it and try again", 409, ErrorCodes.NOTIFICATION_ERROR);

        var retryCount = observedRetryCount + 1;

        // 重置失败的收件人：只碰此刻仍是 Failed 的那些，别处刚记成 Sent 的不动。
        var failedRecipients = await _recipientRepository.AsQueryable()
            .Where(r => r.MessageId == messageId && r.Status == NotificationStatus.Failed)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Status, NotificationStatus.Pending)
                      .SetProperty(r => r.FailureReason, (string?)null),
                cancellationToken);

        var cancelledRecipientCount = await _recipientRepository.AsQueryable()
            .CountAsync(r => r.MessageId == messageId && r.Status == NotificationStatus.Cancelled, cancellationToken);
        if (cancelledRecipientCount > 0)
        {
            Logger.LogInformation(
                "Retrying notification {NotificationId}: {FailedCount} failed recipients will be retried, {CancelledCount} cancelled recipients will be skipped",
                messageId, failedRecipients, cancelledRecipientCount);
        }

        // 通过队列调度重试（带退避延迟）
        if (_queueService != null)
        {
            var delay = TimeSpan.FromSeconds(delaySeconds);
            LogInformation("Retrying notification {NotificationId} via queue with {DelaySeconds}s delay (retry {RetryCount}/{MaxRetryCount})",
                messageId, delaySeconds, retryCount, notification.MaxRetryCount);
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
    /// ★★ <b>闸门不是性能优化。</b>每条 <see cref="RetryAsync"/> 各发几次查询与条件更新、
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
