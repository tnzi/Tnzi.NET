using Message = Tnzi.Notification.Entities.Message;

namespace Tnzi.Notification.Services;

/// <summary>
/// 通知服务实现（创建+发送编排）
/// </summary>
public class NotificationService : ApplicationService, INotificationService
{
    private readonly IRepository<Message, Guid> _notificationRepository;
    private readonly IRepository<Recipient, Guid> _recipientRepository;
    private readonly RecipientChannelDispatcher _dispatcher;
    private readonly RecipientEligibility _eligibility;
    private readonly INotificationQueueService? _queueService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOptionsMonitor<NotificationOptions> _optionsMonitor;
    private readonly INotificationOptOutService _optOutService;
    private readonly INotificationPreferenceService _preferenceService;
    private readonly INotificationProviderResolver _providers;
    private readonly INotificationProviderSelector _providerSelector;
    private readonly ITemplateRenderService? _templateRenderService;
    private readonly ICurrentTenant? _currentTenant;
    private readonly IFileContentReader? _fileContentReader;
    private readonly IFileReadAccessProbe? _fileReadAccessProbe;
    private readonly NotificationSendPacer _pacer;

    // 静态信号量，确保跨请求的并发控制真正生效
    private static SemaphoreSlim? _sendSemaphore;
    private static readonly object _semaphoreLock = new();

    private NotificationOptions Options => _optionsMonitor.CurrentValue;

    /// <summary>
    /// 入队时随工作项带走的租户。与 <c>AuditPropertyHelper</c> 落库时写进 <c>Message.TenantId</c>
    /// 的是同一个表达式，所以工作项切回去的正是那行所在的租户。
    /// </summary>
    private Guid? CurrentTenantId => _currentTenant?.Id ?? CurrentUser?.TenantId;

    public NotificationService(
        IRepository<Message, Guid> notificationRepository,
        INotificationProviderResolver providers,
        INotificationProviderSelector providerSelector,
        IUnitOfWork unitOfWork,
        IOptionsMonitor<NotificationOptions> optionsMonitor,
        IServiceProvider serviceProvider,
        INotificationOptOutService optOutService,
        INotificationPreferenceService preferenceService,
        IRepository<Recipient, Guid> recipientRepository,
        INotificationQueueService? queueService = null,
        ITemplateRenderService? templateRenderService = null,
        ICurrentTenant? currentTenant = null,
        IFileContentReader? fileContentReader = null,
        IFileReadAccessProbe? fileReadAccessProbe = null,
        TimeProvider? timeProvider = null,
        NotificationSendPacer? sendPacer = null)
        : base(serviceProvider)
    {
        _notificationRepository = Check.NotNull(notificationRepository);
        // 取消的级联是一条按状态带条件的批量更新（见 CancelAsync），要直接对收件人表下手。
        _recipientRepository = Check.NotNull(recipientRepository);
        _unitOfWork = Check.NotNull(unitOfWork);
        _optionsMonitor = Check.NotNull(optionsMonitor);
        // 发送器不再直接注入：一条渠道可以有多个（默认 + 具名），派发时按 Message.ProviderKey
        // 经 INotificationProviderResolver 取。四条渠道的默认发送器仍由本模块无条件注册
        // （传真未配置时是 UnconfiguredFaxSender），取不到时那一次投递失败而不是落进别的分支。
        _providers = Check.NotNull(providers);
        _providerSelector = Check.NotNull(providerSelector);
        // 两个核心契约都随 Tnzi.Storage 注册，未加载存储模块时为 null：
        // 那时只带 FileId 的附件没人解析得出字节，创建那一刻就拒绝（见 BuildAttachmentsAsync）。
        _fileContentReader = fileContentReader;
        _fileReadAccessProbe = fileReadAccessProbe;
        _dispatcher = new RecipientChannelDispatcher(
            _providers,
            _optionsMonitor,
            Check.NotNull(optOutService),
            Logger,
            fileContentReader);
        // 必需而非可选：本模块自己无条件注册它，缺了就该在容器里立刻炸，
        // 而不是让退订在运行时静默失效 —— 后者恰恰是这条修复要终结的形态。
        _optOutService = Check.NotNull(optOutService);
        // 同上：本模块自己无条件注册它，缺了就该在容器里立刻炸，
        // 而不是让「用户关掉的通知照发」在运行时静默成立。
        _preferenceService = Check.NotNull(preferenceService);
        // 「谁还应该收到这条消息」的三道过滤全在这里，顺序表只有一份。
        _eligibility = new RecipientEligibility(
            _notificationRepository, _unitOfWork, _optOutService, _preferenceService, Logger,
            timeProvider ?? TimeProvider.System);
        _queueService = queueService;
        _templateRenderService = templateRenderService;
        _currentTenant = currentTenant;
        // 模块把它注册成单例：限速管的是发送账号，一次群发、一次重发失败项、一次恢复续发打的是同一个账号。
        // 没注册（自行组装服务的消费方、测试）时退回本实例自己的一份，至少一次发送之内仍按配置限速。
        _pacer = sendPacer ?? new NotificationSendPacer(_optionsMonitor, timeProvider);

        EnsureSemaphoreInitialized(Options.MaxConcurrency);
    }

    public async Task<Result<NotificationInfo>> CreateAndSendAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var createResult = await CreateCoreAsync(request, QueuedAt(request), cancellationToken);
        if (!createResult.Succeeded)
            return Fail<NotificationInfo>(createResult.Message ?? "Failed to create notification", createResult.Code ?? 400, createResult.ErrorCode);

        if (createResult.Data == null)
            return Fail<NotificationInfo>("Notification data is null after creation", 500, ErrorCodes.NOTIFICATION_ERROR);

        var notificationInfo = createResult.Data;

        var delivery = await StartDeliveryAsync(notificationInfo.Id, request, cancellationToken);
        if (!delivery.Succeeded)
            return Fail<NotificationInfo>(delivery.Message ?? "Failed to send notification", delivery.Code ?? 400, delivery.ErrorCode);

        return Ok(notificationInfo, delivery.Message ?? "Notification created and queued successfully");
    }

    public async Task<Result<IEnumerable<NotificationInfo>>> CreateManyAndSendAsync(IEnumerable<CreateNotificationRequest> requests, CancellationToken cancellationToken = default)
    {
        Check.NotNull(requests);
        var requestList = requests.ToList();
        if (requestList.Count == 0)
            return Ok(Enumerable.Empty<NotificationInfo>());

        var messageRequestMap = new List<(Message Message, CreateNotificationRequest Request)>();
        var errors = new List<string>();

        for (int i = 0; i < requestList.Count; i++)
        {
            var request = requestList[i];
            try
            {
                // ★★ 与 CreateAsync 共用同一份构造：此前这里自己拼 Message，不抄 ScheduledTime、状态恒为 Pending，
                // 于是批量排给明早的提醒当场就发出去了。两条入口对同一份请求必须做同一件事。
                var built = await BuildMessageAsync(request, QueuedAt(request), cancellationToken);
                if (!built.Succeeded)
                {
                    errors.Add($"Request at index {i}: {built.Message}");
                    continue;
                }

                messageRequestMap.Add((built.Data!, request));
            }
            catch (Exception ex)
            {
                var errorMsg = $"Request at index {i}: {ex.Message}";
                errors.Add(errorMsg);
                Logger.LogError(ex, "Error processing request in batch create: {Error}", errorMsg);
            }
        }

        if (messageRequestMap.Count == 0)
        {
            var errorMessage = errors.Count > 0
                ? $"No valid messages created in batch. Errors: {string.Join("; ", errors)}"
                : "No valid messages created in batch";
            return Fail<IEnumerable<NotificationInfo>>(errorMessage, 400);
        }

        var messages = messageRequestMap.Select(m => m.Message).ToList();
        await _notificationRepository.InsertManyAsync(messages, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var message in messages)
            await PublishCreatedAsync(message);

        foreach (var (message, request) in messageRequestMap)
        {
            var delivery = await StartDeliveryAsync(message.Id, request, cancellationToken);
            if (!delivery.Succeeded)
                Logger.LogWarning("Failed to send notification {NotificationId} immediately: {Error}", message.Id, delivery.Message);
        }

        var successMessage = errors.Count > 0
            ? $"Processed {messages.Count} messages successfully, {errors.Count} failed: {string.Join("; ", errors)}"
            : $"Processed {messages.Count} messages successfully";

        LogInformation("Batch created and queued {Count} notifications. Errors: {ErrorCount}", messages.Count, errors.Count);
        var notificationInfos = RecipientAddressMask.Apply(messages.MapToList<NotificationInfo>());
        return Ok((IEnumerable<NotificationInfo>)notificationInfos, successMessage);
    }

    public Task<Result<NotificationInfo>> CreateAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        // 只创建、不要求发送：不写 ScheduledTime，派发恢复永远不碰它（见 QueuedAt）。
        return CreateCoreAsync(request, queuedAt: null, cancellationToken);
    }

    private async Task<Result<NotificationInfo>> CreateCoreAsync(CreateNotificationRequest request, DateTime? queuedAt, CancellationToken cancellationToken)
    {
        var built = await BuildMessageAsync(request, queuedAt, cancellationToken);
        if (!built.Succeeded)
            return Fail<NotificationInfo>(built.Message ?? "Notification could not be created", built.Code ?? 400, ErrorCodes.NOTIFICATION_ERROR);

        var notification = built.Data!;

        await _notificationRepository.InsertAsync(notification, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await PublishCreatedAsync(notification);

        LogInformation("Notification created: {NotificationId}, Type: {Type}, Recipients: {Count}",
            notification.Id, notification.Type, notification.TotalRecipientCount);

        // 推送渠道的「地址」是设备令牌，对外一律掩码（见 RecipientAddressMask）。
        return Ok(RecipientAddressMask.Apply(notification.MapTo<NotificationInfo>()));
    }

    public Task<Result> SendAsync(Guid messageId, CancellationToken cancellationToken = default)
        => SendCoreAsync(messageId, claimedByCaller: false, cancellationToken);

    /// <summary>
    /// 发送一条<b>调用方已经认领过</b>（条件更新到 <see cref="NotificationStatus.Sending"/>）的消息。
    /// </summary>
    /// <remarks>
    /// 派发恢复的几遍扫描在交给发送路径之前各自做过一次条件认领（见 <see cref="MessageClaim"/> 与卡住批次的租约），
    /// 手上拿着的就是那份认领。走 <see cref="SendAsync"/> 会被自己的认领挡在门外：它对一条 <c>Sending</c> 的消息答 409。
    /// </remarks>
    internal Task<Result> SendClaimedAsync(Guid messageId, CancellationToken cancellationToken = default)
        => SendCoreAsync(messageId, claimedByCaller: true, cancellationToken);

    private async Task<Result> SendCoreAsync(Guid messageId, bool claimedByCaller, CancellationToken cancellationToken)
    {
        // Use tracking query to avoid conflict when entity is already tracked
        // (e.g., CreateAndSendAsync inserts then immediately sends)
        var notification = await _notificationRepository
            .AsQueryable(withTracking: true)
            .Include(n => n.Recipients)
            .Include(n => n.Attachments)
            .FirstOrDefaultAsync(n => n.Id == messageId, cancellationToken);

        if (notification == null)
            return Fail($"Notification {messageId} not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (notification.Status == NotificationStatus.Sent)
            return Fail($"Notification {messageId} has already been sent", 400, ErrorCodes.NOTIFICATION_ERROR);

        if (notification.Status == NotificationStatus.Cancelled)
            return Fail($"Notification {messageId} has been cancelled", 400, ErrorCodes.NOTIFICATION_ERROR);

        // If scheduled and not yet time, skip
        if (notification.Status == NotificationStatus.Scheduled && notification.ScheduledTime.HasValue && notification.ScheduledTime.Value > DateTime.UtcNow)
            return Fail($"Notification {messageId} is scheduled for {notification.ScheduledTime.Value:u}", 400, ErrorCodes.NOTIFICATION_ERROR);

        // ★★★ 先抢到这条消息，再发。发送入口不止一个（排队 / 定时器 / 延迟重试 / 管理端 {id}/send /
        // 恢复扫描），它们看到的是同一行；此前这里只拒绝 Sent / Cancelled / 未到点，然后**无条件**写 Sending。
        // 于是恢复扫描先认领了一条到期的定时消息、定时器的工作项在单读者队列里排到之后才执行时，
        // 它看到 Sending 照样往下走，把还没分片落库的收件人（库里仍是 Pending）再发一遍。
        // 现在 Sending 一律答 409；其余状态经一次条件更新抢到才往下走，两个同时到达的入口只有一个抢得到。
        if (claimedByCaller)
        {
            if (notification.Status != NotificationStatus.Sending)
                return Fail($"Notification {messageId} is no longer held by this dispatch pass (status {notification.Status}); nothing was sent", 409, ErrorCodes.NOTIFICATION_ERROR);
        }
        else
        {
            if (notification.Status == NotificationStatus.Sending)
                return Fail($"Notification {messageId} is already being sent", 409, ErrorCodes.NOTIFICATION_ERROR);

            if (!await MessageClaim.TryClaimAsync(_notificationRepository, notification.Id, notification.Status, cancellationToken))
                return Fail($"Notification {messageId} was taken by another sender; it is not sent twice", 409, ErrorCodes.NOTIFICATION_ERROR);
        }

        // ★ RetryCount 不在这里加：它数的是「失败之后又重试了几次」（由 RetryAsync 计），不是这条路径被走了几遍。
        //   此前每次进入都加一：第一次发送、恢复扫描续发、静默时段延后到点的那一遍都各算一次，
        //   一条延后过两次的消息还没真正重试过就到了 MaxRetryCount，之后真失败了「重试」与「重试全部失败」都不再接手。
        // ★★ 状态与心跳都已由认领落库（同一条条件更新，进程在循环中途退出时行停在 Sending，卡住批次那一遍扫得到它）。
        //   被跟踪的那份消息的 Status 刻意不跟着改、也不在这里保存：改了它就是一列待写的修改，下一次保存
        //   会把 Sending 无条件写回去，认领之后、保存之前落下的取消或删除于是被盖掉，剩下的人照发不误。
        //   这一列此后只由 MessageClaim 的条件更新改写（理由见那里），被跟踪的那份停在加载时的值，任何保存都不碰它。

        var dueNow = DateTime.UtcNow;
        var pendingRecipients = notification.Recipients
            .Where(r => r.Status == NotificationStatus.Pending
                        || r.Status == NotificationStatus.Failed
                        // 静默时段到期的那些：它们被延后而不是被丢弃，现在轮到它们了。
                        || (r.Status == NotificationStatus.Scheduled
                            && r.DeferredUntil != null
                            && r.DeferredUntil <= dueNow))
            .ToList();

        // 退订与偏好都在发送那一刻判定（见 RecipientEligibility）：定时与排队的消息可能
        // 几天后才发出去，这些事随时可能发生在这中间。
        var candidateCount = pendingRecipients.Count;
        var (sendable, deferredCount) = await _eligibility.FilterAsync(notification, pendingRecipients, cancellationToken);
        pendingRecipients = sendable;
        // 前三道过滤把人全部拦光了（退订 / 关掉了这个渠道 / 到了每小时上限），
        // 与「本来就没有待发收件人」不是一回事，也与「都被挪到晚点了」不是一回事。
        var everyoneFilteredOut = pendingRecipients.Count == 0 && candidateCount > deferredCount;

        if (pendingRecipients.Count == 0)
        {
            // ★★ 三种「现在没人可发」要分开答，因为它们的下文完全不同：
            //   ① 有人还排在延后里 → 这条消息还有下文，状态回到 Scheduled 等延后那一遍接手，
            //      **不能**盖 SentTime（那会让一条还没送到的消息显示成已发送）。
            //   ② 全员被拦   → 到此为止，Cancelled。
            //   ③ 本来就没有待发收件人 → 维持原有语义，Sent。
            // ★★ ① 要看**整条消息**的收件人，不只是这一轮新延后的人数：上一轮就延后、还没到点的那些
            // 不在候选里（只挑到期的），deferredCount 数不到他们。只看 deferredCount 时，一条等着延后
            // 到点的消息被再发一次（管理端「发送」、到期扫描）就会写成 Sent：而延后那一遍只看
            // Scheduled 的消息，到点再也看不见它，被延后的人永远收不到。与下面主循环的判据同一份。
            var stillDeferred = deferredCount > 0 || HasDeferredRecipients(notification);

            // ★★ 「全员被拦」只说这一轮：续发的批次、延后到点的那一遍之前可能已经送到过别人。
            //   此前只看这一轮，九百九十人昨晚已送达、剩下十个被延后的人到点时被上限拦下，整条消息就成了 Cancelled。
            //   有人收到过的消息按已发送收尾：这一轮被拦下的人标 Cancelled，与主循环里被过滤掉的人同一个处置，不算失败。
            var deliveredEarlier = notification.Recipients.Any(r => r.Status == NotificationStatus.Sent);

            var emptyOutcome = stillDeferred
                ? NotificationStatus.Scheduled
                : everyoneFilteredOut && !deliveredEarlier ? NotificationStatus.Cancelled : NotificationStatus.Sent;

            if (!await FinishAsync(notification, emptyOutcome, stillDeferred ? notification.SentTime : DateTime.UtcNow, notification.FailureReason, cancellationToken))
                return Fail(StoppedWhileSendingMessage(messageId, 0), 409, ErrorCodes.NOTIFICATION_ERROR);

            if (deferredCount > 0)
                return Ok($"{deferredCount} recipient(s) are inside their quiet hours; delivery was deferred, not dropped.");

            if (stillDeferred)
                return Ok("No recipient is due yet; the remaining recipients are deferred by their quiet hours and will be sent when those end.");

            return everyoneFilteredOut
                // 具体是哪一道拦下了谁，逐条写在收件人的 FailureReason 里。
                ? Ok("No recipient was eligible; nothing was sent. See the delivery report for the reason per recipient.")
                : Ok("No pending recipients to send to");
        }

        var successCount = 0;
        var failureCount = 0;
        string? lastError = null;

        // ★★ 投递结果必须在循环**内**分片落库（理由见 SendProgress）：整批只在末尾保存一次时，
        // 库里所有收件人在几十分钟的群发期间一直写着 Pending、消息的 LastModificationTime
        // 一直停在进循环之前，于是恢复扫描会把这条正在飞的批次判成「卡住」接手过去，
        // 把已经发出去的那些全部再发一遍。
        var progress = new SendProgress(_notificationRepository, _unitOfWork, notification);

        foreach (var recipient in pendingRecipients)
        {
            // ★★ 限速落在每个收件人的投递上（Dispatch:RatePerMinute，见 NotificationSendPacer）。
            // 先等槽再占并发名额：等待期间不该占着一个并发位。
            await progress.WaitAsync(_pacer.Reserve(notification.Type, notification.ProviderKey), cancellationToken);

            // ★★ 管理端的取消与删除发生在另一个作用域里，而这份名单是开跑时读出来的：
            // 每投递一个收件人之前问一次库，消息已不在发送中就停在这里，剩下的人一个都不发。
            if (!await progress.StillSendingAsync(cancellationToken))
                break;

            await _sendSemaphore!.WaitAsync(cancellationToken);
            try
            {
                var sendResult = await _dispatcher.DispatchAsync(notification, recipient, cancellationToken);
                if (sendResult.Success)
                {
                    RecordSent(recipient, sendResult);
                    successCount++;
                }
                else
                {
                    lastError = RecordFailed(recipient, sendResult.FailureReason);
                    failureCount++;
                }
            }
            catch (Exception ex)
            {
                lastError = RecordFailed(recipient, ex.Message);
                failureCount++;
                Logger.LogError(ex, "Error sending notification {NotificationId} to {Address}", messageId, recipient.Address);
            }
            finally
            {
                _sendSemaphore!.Release();
            }

            await progress.RecordAsync(cancellationToken);
        }

        // 更新消息统计
        // lastError 只可能来自 RecordFailed 的返回值，已经收敛到 FailureReason 的列宽。
        notification.SuccessCount = notification.Recipients.Count(r => r.Status == NotificationStatus.Sent);
        notification.FailureCount = notification.Recipients.Count(r => r.Status == NotificationStatus.Failed);

        // ★★ 还有人排在静默时段之后时，这条消息**没有结束**：状态回到 Scheduled，
        // 到期扫描会把剩下那些发完。写成 Sent 就是一条「已发送」而实际上还没送到的记录，
        // 也会让恢复扫描永远看不见它。
        NotificationStatus outcome;
        var sentTime = notification.SentTime;
        var failureReason = notification.FailureReason;
        if (HasDeferredRecipients(notification))
        {
            outcome = NotificationStatus.Scheduled;
            failureReason = notification.FailureCount > 0 ? lastError : failureReason;
        }
        else if (notification.FailureCount == 0)
        {
            outcome = NotificationStatus.Sent;
            sentTime = DateTime.UtcNow;
        }
        else if (notification.SuccessCount > 0)
        {
            outcome = NotificationStatus.PartiallySent;
            sentTime = DateTime.UtcNow;
            failureReason = lastError;
        }
        else
        {
            outcome = NotificationStatus.Failed;
            failureReason = lastError;
        }

        // 收尾同样是条件更新：这一轮里被取消或删除的消息，不再被写回成已发送（也不发「已发送」事件）。
        if (!await FinishAsync(notification, outcome, sentTime, failureReason, cancellationToken))
            return Fail(StoppedWhileSendingMessage(messageId, successCount), 409, ErrorCodes.NOTIFICATION_ERROR);

        // 发布事件
        if (EventBus != null)
        {
            if (outcome == NotificationStatus.Failed)
            {
                await EventBus.PublishAsync(new NotificationFailedEvent
                {
                    MessageId = notification.Id,
                    Type = notification.Type,
                    FailureReason = failureReason ?? "Unknown error",
                    RetryCount = notification.RetryCount,
                    MaxRetryCount = notification.MaxRetryCount
                });
            }
            else
            {
                await EventBus.PublishAsync(new NotificationSentEvent
                {
                    MessageId = notification.Id,
                    Type = notification.Type,
                    SuccessCount = successCount,
                    FailureCount = failureCount,
                    SentTime = DateTime.UtcNow
                });
            }
        }

        LogInformation("Notification {NotificationId} sent: {SuccessCount} success, {FailureCount} failed",
            messageId, successCount, failureCount);

        return outcome == NotificationStatus.Failed
            ? Fail($"Failed to send notification: {lastError}", 500, ErrorCodes.NOTIFICATION_ERROR)
            : Ok("Notification sent successfully");
    }

    /// <summary>
    /// 取消一条还没发完的消息。正在发送中的消息同样可以取消：发送循环每投递一个收件人之前都会问一次库，
    /// 看到取消就停下，剩下的人一个都不发（已经发出去的那些照实记成 Sent）。
    /// </summary>
    /// <remarks>
    /// ★★ 状态改写是一次条件更新（从刚才读到的状态改成 <c>Cancelled</c>），收件人的级联是一条按状态带条件的批量更新：
    /// 此前这里把整行读出来、改完再整行写回，既会被正在飞的发送循环的整行写回盖掉，也会反过来把发送循环刚记下的
    /// 计数与「已发送」盖回去。正在投递的那一个收件人此刻在库里仍是 <c>Pending</c>，会被标成 <c>Cancelled</c>，
    /// 随后发送循环把它照实记成 <c>Sent</c> 或 <c>Failed</c>。
    /// </remarks>
    public async Task<Result> CancelAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var status = await _notificationRepository.AsQueryable()
            .Where(m => m.Id == messageId)
            .Select(m => (NotificationStatus?)m.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (status == null)
            return Fail($"Notification {messageId} not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (status == NotificationStatus.Sent || status == NotificationStatus.PartiallySent)
            return Fail($"Notification {messageId} has already been sent and cannot be cancelled", 400, ErrorCodes.NOTIFICATION_ERROR);

        if (status == NotificationStatus.Cancelled)
            return Ok("Notification is already cancelled");

        var cancelledRecipients = await TryCancelFromAsync(messageId, status.Value, cancellationToken);
        if (cancelledRecipients == null)
            return Fail($"Notification {messageId} changed state while it was being cancelled; reload it and try again", 409, ErrorCodes.NOTIFICATION_ERROR);

        LogInformation("Notification cancelled: {NotificationId} (was {Status}, {Count} recipient(s) not sent)",
            messageId, status.Value, cancelledRecipients);
        return Ok("Notification cancelled successfully");
    }

    /// <summary>
    /// 批量取消还没开始发送的消息（<c>Pending</c> / <c>Scheduled</c>）。
    /// </summary>
    /// <remarks>
    /// ★★ 与 <see cref="CancelAsync"/> 同一种写法：每条消息的状态是一次条件更新（从读到的状态改成 <c>Cancelled</c>），
    /// 收件人的级联是按状态带条件的批量更新。此前这里把整批消息连同收件人读进来、改完再 <c>UpdateManyAsync</c> 整图写回：
    /// 读与写之间被排队的工作项或派发恢复认领走、已经送达了几个人的消息，会被写成 <c>Cancelled</c>，
    /// 刚记成 <c>Sent</c> 的收件人连同 <c>SentTime</c> / 外部消息号一起被写回 <c>Cancelled</c>，投递报告从此说人没收到。
    /// 读完之后状态已经变了的消息（被认领去发送了）原样留下，不计入取消的条数。
    /// </remarks>
    public async Task<Result<int>> BatchCancelAsync(List<Guid> ids, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(ids);

        var candidates = await _notificationRepository.AsQueryable()
            .Where(n => ids.Contains(n.Id) && (n.Status == NotificationStatus.Pending || n.Status == NotificationStatus.Scheduled))
            .Select(n => new { n.Id, n.Status })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return Ok(0, "No cancellable notifications found");

        var cancelled = 0;
        foreach (var candidate in candidates)
        {
            if (await TryCancelFromAsync(candidate.Id, candidate.Status, cancellationToken) != null)
                cancelled++;
        }

        var changed = candidates.Count - cancelled;
        LogInformation("Batch cancelled {Count} notifications ({Changed} changed state before they could be cancelled)", cancelled, changed);
        return changed > 0
            ? Ok(cancelled, $"{cancelled} notifications cancelled; {changed} started sending before they could be cancelled and were left as they are")
            : Ok(cancelled, $"{cancelled} notifications cancelled");
    }

    /// <summary>
    /// 把一条消息从 <paramref name="from"/> 改成 <c>Cancelled</c>（条件更新），成功时把还没发的收件人一并取消。
    /// 返回取消的收件人数；消息此刻已不是 <paramref name="from"/> 时什么都不写，返回 <see langword="null"/>。
    /// </summary>
    private async Task<int?> TryCancelFromAsync(Guid messageId, NotificationStatus from, CancellationToken cancellationToken)
    {
        if (!await MessageClaim.TryTransitionAsync(_notificationRepository, messageId, from, NotificationStatus.Cancelled, cancellationToken))
            return null;

        // Cascade: recipients not sent yet are cancelled. 带条件的批量更新：已经由发送循环记成 Sent / Failed 的不动。
        return await _recipientRepository.AsQueryable()
            .Where(r => r.MessageId == messageId
                        && (r.Status == NotificationStatus.Pending || r.Status == NotificationStatus.Scheduled))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, NotificationStatus.Cancelled), cancellationToken);
    }

    public async Task<Result<NotificationPreviewDto>> PreviewAsync(PreviewNotificationRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        // 收件人可选：预览渲染的是内容。给了就按创建时的同一条规则校验，免得预览通过、真发时才 400。
        if (request.Recipients is { Count: > 0 })
        {
            var validationError = ValidateRecipients(request.Recipients);
            if (validationError != null)
                return Fail<NotificationPreviewDto>(validationError, 400, ErrorCodes.NOTIFICATION_ERROR);
        }

        var source = ContentSource.Of(request);
        var rendered = await RenderContentAsync(source, cancellationToken);
        if (!rendered.Succeeded)
            return Fail<NotificationPreviewDto>(rendered.Message ?? "Notification content could not be rendered", rendered.Code ?? 400, ErrorCodes.NOTIFICATION_ERROR);

        var (subject, content, category) = rendered.Data;

        return Ok(new NotificationPreviewDto
        {
            Subject = subject,
            Content = content,
            // ★ 按渲染时实际用的输出类型回报，不是回显入参：IsHtml 默认 true，短信 / 推送的预览不带它时
            //   会被当成 HTML 放进预览框，换行全部塌掉，而发出去的是纯文本。
            IsHtml = ResolveOutputKind(source) switch
            {
                TemplateOutputKind.PlainText => false,
                TemplateOutputKind.Html => true,
                _ => request.IsHtml
            },
            Category = category,
            RecipientCount = request.Recipients?.Count ?? 0,
            TemplateName = request.TemplateName
        });
    }

    public async Task<Result<int>> ResendToFailedRecipientsAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.AsQueryable(withTracking: true)
            .Include(m => m.Recipients)
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (notification == null)
            return Fail<int>($"Notification {messageId} not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var failedRecipients = notification.Recipients
            .Where(r => r.Status == NotificationStatus.Failed)
            .ToList();

        if (failedRecipients.Count == 0)
            return Ok(0, "No failed recipients to resend to");

        // ★★ 先抢到这条消息，再重发：理由与 SendCoreAsync 逐字相同：这条路径绕开 SendAsync 自己投递，
        // 管理端连点两下就是两个并发的重发，各自把同一批失败的收件人再发一遍。认领之后消息在整个循环里
        // 是 Sending（心跳由 SendProgress 推进），进程中途退出时卡住扫描接得回来。
        if (notification.Status == NotificationStatus.Sending)
            return Fail<int>($"Notification {messageId} is already being sent", 409, ErrorCodes.NOTIFICATION_ERROR);

        if (!await MessageClaim.TryClaimAsync(_notificationRepository, notification.Id, notification.Status, cancellationToken))
            return Fail<int>($"Notification {messageId} was taken by another sender; it is not sent twice", 409, ErrorCodes.NOTIFICATION_ERROR);

        // 被跟踪的那份不跟着改 Status、不在这里保存（理由同 SendCoreAsync）：认领之后落下的取消或删除不会被它盖掉。

        // 重发同样要过四道过滤：上次失败之后对方可能已经退订，而这条路径绕开 SendAsync。
        // ★★ 过滤放在认领之后，与 SendCoreAsync 同序：静默时段那一道会把人延后（Scheduled + DeferredUntil），
        // 这条消息于是还没有结束，收尾必须看见它们。此前过滤在认领之前、全员被延后时直接答「没有失败的收件人」，
        // 消息原样停在 Failed / PartiallySent：延后那一遍只看 Scheduled 的消息，这些人从此收不到。
        var (sendable, deferredCount) = await _eligibility.FilterAsync(notification, failedRecipients, cancellationToken);

        var successCount = 0;
        // 与 SendAsync 同一条理由：整批只在末尾保存一次时，中途崩溃会把已经重发出去的
        // 那些人退回 Failed，下一次「重发失败项」再发一遍。
        var progress = new SendProgress(_notificationRepository, _unitOfWork, notification);

        foreach (var recipient in sendable)
        {
            // 与 SendAsync 同一道限速：重发打到的是同一个发送账号。
            await progress.WaitAsync(_pacer.Reserve(notification.Type, notification.ProviderKey), cancellationToken);

            // 与 SendAsync 同一道取消检查：消息在这一轮里被取消或删除，剩下的人不再重发。
            if (!await progress.StillSendingAsync(cancellationToken))
                break;

            recipient.Status = NotificationStatus.Pending;
            recipient.FailureReason = null;

            await _sendSemaphore!.WaitAsync(cancellationToken);
            try
            {
                var sendResult = await _dispatcher.DispatchAsync(notification, recipient, cancellationToken);
                if (sendResult.Success)
                {
                    RecordSent(recipient, sendResult);
                    successCount++;
                }
                else
                {
                    RecordFailed(recipient, sendResult.FailureReason);
                }
            }
            catch (Exception ex)
            {
                RecordFailed(recipient, ex.Message);
                Logger.LogError(ex, "Error resending notification {NotificationId} to {Address}", messageId, recipient.Address);
            }
            finally
            {
                _sendSemaphore!.Release();
            }

            await progress.RecordAsync(cancellationToken);
        }

        // 更新消息统计
        notification.SuccessCount = notification.Recipients.Count(r => r.Status == NotificationStatus.Sent);
        notification.FailureCount = notification.Recipients.Count(r => r.Status == NotificationStatus.Failed);

        // 认领把状态改成了 Sending，所以每一种结局都要写回。
        // 还有人排在静默时段之后时这条消息没有结束，与 SendAsync 同一判据。
        // 既没有人收到过、也没有人失败（剩下的失败项都被过滤拦下了）：与 SendAsync「全员被拦」同一个结局。
        NotificationStatus outcome;
        if (HasDeferredRecipients(notification))
            outcome = NotificationStatus.Scheduled;
        else if (notification.FailureCount == 0)
            outcome = notification.SuccessCount > 0 ? NotificationStatus.Sent : NotificationStatus.Cancelled;
        else if (notification.SuccessCount > 0)
            outcome = NotificationStatus.PartiallySent;
        else
            outcome = NotificationStatus.Failed;

        if (!await FinishAsync(notification, outcome, notification.SentTime, notification.FailureReason, cancellationToken))
            return Fail<int>(StoppedWhileSendingMessage(messageId, successCount), 409, ErrorCodes.NOTIFICATION_ERROR);

        LogInformation("Resent notification {NotificationId} to {Count} failed recipients, {Success} succeeded, {Deferred} deferred by quiet hours",
            messageId, sendable.Count, successCount, deferredCount);

        var summary = $"{successCount}/{sendable.Count} recipients resent successfully";
        return deferredCount > 0
            ? Ok(successCount, $"{summary}; {deferredCount} recipient(s) are inside their quiet hours and will be sent when those end")
            : Ok(successCount, summary);
    }

    #region Private Methods


    /// <summary>
    /// 把这一轮的结局写回：先按属性保存收件人与两个计数，再用一条条件更新写消息的状态。
    /// 消息在这一轮里被取消或删除了，状态一列都不写，返回 <see langword="false"/>。
    /// </summary>
    /// <remarks>
    /// ★ 被跟踪的那份消息的 <c>Status</c> 刻意<b>不</b>跟着改：从认领到结局，它一直停在加载时的值、从未被标成已修改，
    /// 之后无论谁再保存一次（外层工作单元提交时），这一列都不会被写，盖不掉别处的取消、删除与结局。
    /// </remarks>
    private async Task<bool> FinishAsync(Message notification, NotificationStatus outcome, DateTime? sentTime, string? failureReason, CancellationToken cancellationToken)
    {
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (await MessageClaim.TryFinishAsync(_notificationRepository, notification.Id, outcome, sentTime, failureReason, cancellationToken))
            return true;

        LogInformation("Notification {NotificationId} was cancelled or deleted while it was being sent; the deliveries made in this round are recorded and its status is left as is",
            notification.Id);
        return false;
    }

    /// <summary>发送进行到一半时消息被取消或删除：已经发出去的照实记下，剩下的不发，状态留给取消 / 删除的那一方。</summary>
    private static string StoppedWhileSendingMessage(Guid messageId, int sentThisRound)
        => $"Notification {messageId} was cancelled or deleted while it was being sent; {sentThisRound} recipient(s) had already been sent and the rest were not";

    /// <summary>
    /// 把一次成功投递记到收件人上。
    /// </summary>
    /// <remarks>
    /// ★ 网关给回的外部消息号超长即<b>丢弃</b>而不截断（理由见
    /// <see cref="NotificationFieldLimits"/>），但这不改变「已经发出去了」这件事 ——
    /// 只是回执再也对不上这一次投递，所以要留一条警告。
    /// <para>
    /// ★★ <b><c>FailureReason</c> 必须一并清掉。</b>重试与续发都会在同一行上重跑，
    /// 上一次的失败说明留在那里，投递报告里就出现一条 <c>Status = Sent</c> 却带着
    /// 「SMTP 550 拒收」的记录 —— 读的人无从判断这封到底送到没有，而这正是本模块
    /// 一直在消灭的形态：记下来的结果与实际发生的事不符。
    /// </para>
    /// </remarks>
    private void RecordSent(Recipient recipient, SendResult sendResult)
    {
        recipient.Status = NotificationStatus.Sent;
        recipient.SentTime = DateTime.UtcNow;
        recipient.FailureReason = null;
        recipient.ExternalMessageId =
            NotificationFieldLimits.AcceptExternalMessageId(sendResult.ExternalMessageId, out var dropped);

        if (dropped != null)
        {
            Logger.LogWarning(
                "Sender returned a {Length}-character external message id for recipient {RecipientId}, past the {Limit}-character column. It was dropped, so delivery confirmations can no longer be matched to this send.",
                dropped.Length, recipient.Id, NotificationFieldLimits.ExternalMessageIdMaxLength);
        }
    }

    /// <summary>
    /// 把一次失败记到收件人上，返回<b>已收敛到列宽</b>的原因，供消息级的 lastError 复用。
    /// </summary>
    /// <remarks>
    /// ★ 这里是所有失败原因共同的落库出口，收敛必须发生在这一处而不是各个发送器里：
    /// 发送器契约是公开可替换的，消费应用换一个实现就绕过去了。整批投递结果由一次
    /// SaveChanges 落库，一条越界会连带丢掉这一批已经真的发出去的那些人的状态，
    /// 后果是<b>重复投递</b>而不只是少一行原因。
    /// </remarks>
    private static string? RecordFailed(Recipient recipient, string? reason)
    {
        var bounded = NotificationFieldLimits.TruncateFailureReason(reason);
        recipient.Status = NotificationStatus.Failed;
        recipient.FailureReason = bounded;
        return bounded;
    }

    /// <summary>
    /// 还有收件人排在静默时段之后（<c>Scheduled</c> + <c>DeferredUntil</c>）：这条消息没有结束，
    /// 状态要留在 <c>Scheduled</c> 等延后那一遍接手。
    /// </summary>
    private static bool HasDeferredRecipients(Message notification)
        => notification.Recipients.Any(r => r.Status == NotificationStatus.Scheduled && r.DeferredUntil != null);

    private static void EnsureSemaphoreInitialized(int maxConcurrency)
    {
        if (_sendSemaphore != null) return;
        lock (_semaphoreLock)
        {
            _sendSemaphore ??= new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }
    }

    private static string? ValidateRecipients(List<RecipientInput> recipients)
    {
        if (recipients == null || recipients.Count == 0)
            return "At least one recipient is required";

        for (int i = 0; i < recipients.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(recipients[i].Address))
                return $"Recipient at index {i} has empty address";
        }

        return null;
    }

    /// <summary>
    /// 定下这条消息由哪个服务商投递：请求显式指定的键优先，没指定就问 <see cref="INotificationProviderSelector"/>，
    /// 它也不选就是默认发送器（<see langword="null"/>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>两种来路的键都在这里校验，未注册的一律让创建失败</b>，而不是留到后台派发才逐收件人失败：
    /// 那时调用方早就拿到 200 走了。请求给错键是调用方的错（400）；选择器给错键是消费方接线的错（500，
    /// 原因里指名是选择器给的），两者都不会静默换一家发出去。
    /// </para>
    /// <para>
    /// 选择器只在请求<b>没有</b>指定键时才被问：显式指定表达的是「这条就要走这家」，
    /// 让一条通用规则盖过它，调用方指定就没有意义了。显式写 <c>default</c> 也算指定 ——
    /// 它落库仍是 <see langword="null"/>，但选择器不再被问。
    /// </para>
    /// </remarks>
    private async Task<Result<string?>> ResolveProviderKeyAsync(CreateNotificationRequest request, string category, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ProviderKey))
        {
            var explicitKey = NotificationProviderKeys.Normalize(request.ProviderKey);
            if (explicitKey != null && NotificationProviderKeys.Describe(explicitKey) is { } shapeError)
                return Fail<string?>(shapeError, 400, ErrorCodes.NOTIFICATION_ERROR);

            if (ProviderConfigurationError(request.Type, explicitKey) is { } explicitConfigError)
                return Fail<string?>(explicitConfigError, 500, ErrorCodes.NOTIFICATION_ERROR);

            if (!_providers.IsRegistered(request.Type, explicitKey))
                return Fail<string?>(NotificationProviderProfiles.NotRegisteredMessage(request.Type, explicitKey), 400, ErrorCodes.NOTIFICATION_ERROR);

            return Ok<string?>(explicitKey);
        }

        var context = new NotificationProviderSelectionContext
        {
            Type = request.Type,
            Category = category,
            IsTransactional = request.IsTransactional,
            Priority = request.Priority,
            TemplateName = request.TemplateName,
            SenderId = request.SenderId,
            Recipients = request.Recipients
        };

        var selectedKey = NotificationProviderKeys.Normalize(await _providerSelector.SelectAsync(context, cancellationToken));
        if (selectedKey == null)
            return Ok<string?>(null);

        if (NotificationProviderKeys.Describe(selectedKey) is { } selectedShapeError)
            return Fail<string?>(
                $"{_providerSelector.GetType().Name} selected an invalid provider key: {selectedShapeError}",
                500, ErrorCodes.NOTIFICATION_ERROR);

        if (ProviderConfigurationError(request.Type, selectedKey) is { } selectedConfigError)
            return Fail<string?>(selectedConfigError, 500, ErrorCodes.NOTIFICATION_ERROR);

        if (!_providers.IsRegistered(request.Type, selectedKey))
            return Fail<string?>(
                $"{_providerSelector.GetType().Name} selected a provider that is not registered. "
                + NotificationProviderProfiles.NotRegisteredMessage(request.Type, selectedKey),
                500, ErrorCodes.NOTIFICATION_ERROR);

        return Ok<string?>(selectedKey);
    }

    /// <summary>
    /// 这个键上的发送器是否因为<b>部署配置</b>而构造不出来；构造得出来（或根本没注册）返回 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// ★ 发送器工厂可以抛 <see cref="ConfigurationException"/>：传真节的 <c>EmailProviderKey</c> 指向一个不存在的
    /// 邮件发送器时就是这样（刻意不退回默认发送器，见 <see cref="FaxSenderOptions.EmailProviderKey"/>）。
    /// 这一问在创建路径上，任由它穿出去就是一次 500 异常而不是一个 <see cref="Result"/> 失败 ——
    /// 调用方拿不到那句指向配置项的原因。原因是部署的配置而不是请求，所以是 500 而不是 400。
    /// </remarks>
    private string? ProviderConfigurationError(NotificationType type, string? providerKey)
    {
        try
        {
            _providers.IsRegistered(type, providerKey);
            return null;
        }
        catch (ConfigurationException ex)
        {
            Logger.LogError(ex, "The {Channel} sender for provider {ProviderKey} cannot be constructed from its configuration.",
                type, providerKey ?? NotificationProviderKeys.Default);
            return ex.ConfigurationKey is { } key ? $"{ex.Message} (configuration: {key})" : ex.Message;
        }
    }

    /// <summary>渲染后的消息内容：主题、正文与消息分类。</summary>
    private readonly record struct RenderedContent(string Subject, string Content, string Category);

    /// <summary>渲染一条消息内容要用的输入。创建与预览两条路径各自从请求里取，走同一份渲染规则。</summary>
    private readonly record struct ContentSource(
        NotificationType Type,
        string Subject,
        string Content,
        bool IsHtml,
        string? TemplateName,
        string? LayoutName,
        Dictionary<string, object>? TemplateVariables,
        string? Category)
    {
        public static ContentSource Of(CreateNotificationRequest request)
            => new(request.Type, request.Subject, request.Content, request.IsHtml, request.TemplateName, request.LayoutName, request.TemplateVariables, request.Category);

        public static ContentSource Of(PreviewNotificationRequest request)
            => new(request.Type, request.Subject ?? string.Empty, request.Content ?? string.Empty, request.IsHtml, request.TemplateName, request.LayoutName, request.TemplateVariables, request.Category);
    }

    /// <summary>
    /// 渲染通知内容（使用 ITemplateRenderService 或直接使用请求内容）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ <strong>指定了模板却渲染不出来、而调用方又没给任何原始内容时，返回失败而不是回落。</strong>
    /// 「回落到原始内容」在这种情况下回落到的是两个空串：一条空主题空正文的消息会被落库、
    /// 被发送器照发，调用方拿到成功。2026-09-01 的邀请邮件正是这样丢的 ——
    /// 处理器引用了一个仓库里不存在的模板，被邀请人收到一封没有标题、没有正文、没有链接的信，
    /// 而日志里是「Invitation email sent」。调用方显式给了主题或正文时，回落照旧成立。
    /// </para>
    /// </remarks>
    private async Task<Result<RenderedContent>> RenderContentAsync(ContentSource request, CancellationToken cancellationToken)
    {
        var category = request.Category ?? "General";

        // 没有模板名称，直接使用请求中的内容
        if (string.IsNullOrWhiteSpace(request.TemplateName))
            return Ok(new RenderedContent(request.Subject, request.Content, category));

        var hasRawContent = !string.IsNullOrWhiteSpace(request.Subject) || !string.IsNullOrWhiteSpace(request.Content);

        // 没有模板渲染服务，使用原始内容
        if (_templateRenderService == null)
        {
            if (!hasRawContent)
            {
                return Fail<RenderedContent>(
                    $"Template '{request.TemplateName}' cannot be rendered because the Template module is not loaded, "
                    + "and the request carries no subject or content to fall back to.",
                    500, ErrorCodes.NOTIFICATION_ERROR);
            }

            Logger.LogWarning("ITemplateRenderService not available, using raw content for template '{TemplateName}'", request.TemplateName);
            return Ok(new RenderedContent(request.Subject, request.Content, category));
        }

        // Framework notification templates ship organized by channel
        // (Templates/Notification/{Email|Sms}/{Name}.cshtml), so for these the
        // template Category IS the channel name. When the caller does not set a
        // Category, default the template-lookup category to the channel (from
        // Type) so the shipped templates resolve instead of missing and falling
        // back to an empty body. An explicit Category (a custom grouping) is
        // always honoured. The message's own Category is unaffected (below).
        var templateCategory = string.IsNullOrWhiteSpace(request.Category)
            ? request.Type.ToString()
            : request.Category;

        // 使用 ITemplateRenderService 一站式渲染。出口面由这里告诉渲染服务：
        // 它只知道模板自述的类型，而推送正文与纯文本邮件都不是一种模板类型。
        var renderResult = await _templateRenderService.RenderByNameAsync(
            request.TemplateName,
            "Notification",
            request.TemplateVariables,
            templateCategory,
            request.LayoutName,
            ResolveOutputKind(request),
            cancellationToken);

        if (!renderResult.Succeeded)
        {
            if (!hasRawContent)
            {
                return Fail<RenderedContent>(
                    $"Template '{request.TemplateName}' could not be rendered ({renderResult.Message}), "
                    + "and the request carries no subject or content to fall back to.",
                    renderResult.Code ?? 500, ErrorCodes.NOTIFICATION_ERROR);
            }

            Logger.LogWarning("Template rendering failed for '{TemplateName}': {Error}. Using raw content.", request.TemplateName, renderResult.Message);
            return Ok(new RenderedContent(request.Subject, request.Content, category));
        }

        var rendered = renderResult.Data!;
        var subject = !string.IsNullOrWhiteSpace(rendered.Subject) ? rendered.Subject : request.Subject;
        return Ok(new RenderedContent(subject, rendered.Content, category));
    }

    /// <summary>
    /// 这条消息的正文进的是哪种出口：短信与推送正文恒为纯文本（<c>IsHtml</c> 对它们没有意义，默认值还是 true）；
    /// 邮件按 <c>IsHtml</c>（false = <c>text/plain</c> 部分）；传真的正文只留档不投递，交模板类型决定。
    /// </summary>
    /// <remarks>
    /// <c>@expr</c> 默认 HTML 编码。渲染服务只按 <c>Template.Type == Sms</c> 走纯文本，而 <c>TemplateType</c> 没有 Push
    /// 成员、纯文本邮件也不是一种模板类型 —— 不在这里说清楚，推送里的 <c>@Model.Url</c> 就发成 <c>&amp;amp;</c>，
    /// 发送状态成功、无日志。
    /// </remarks>
    private static TemplateOutputKind? ResolveOutputKind(ContentSource request) => request.Type switch
    {
        NotificationType.Sms or NotificationType.Push => TemplateOutputKind.PlainText,
        NotificationType.Email => request.IsHtml ? TemplateOutputKind.Html : TemplateOutputKind.PlainText,
        _ => null
    };

    /// <summary>
    /// 把请求里的附件变成实体，<b>先过来源纪律</b>（<see cref="AttachmentSourcePolicy"/>），
    /// 再对只带 <c>FileId</c> 的附件问一句「这个人读得到它吗」。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ <c>FilePath</c> 来自请求体，此前原样落库、发信时按它读任意本地文件或取任意 URL —— 持
    /// <c>notification.message.create</c> 的人能把服务端任意文件寄到任意邮箱。拒绝要发生在<b>创建这一刻</b>
    /// （400，一条附件不合规整个请求不落库），不能留到后台发信时逐收件人失败：那时调用方早拿到 200 走了。
    /// 两条创建路径共用这一份，规则不会漂开。
    /// </para>
    /// <para>
    /// ★ <c>FileId</c> 同理：派发时是以<b>系统身份</b>读字节的（<see cref="IFileContentReader"/>，后台没有当前用户），
    /// 所以「这个人能不能引用这份文件」只能在这里问（<see cref="IFileReadAccessProbe"/>）。
    /// <b>有</b>当前用户就必须过探针；<b>没有</b>当前用户的是进程内的框架代码（事件处理器、后台队列），
    /// 管理端点永远带着认证，走不到那一支。没加载存储模块时没人解析得出字节，也在这里拒绝并指名要加载的包 ——
    /// 而不是接受之后在后台逐收件人失败。
    /// </para>
    /// </remarks>
    private async Task<Result<List<Attachment>>> BuildAttachmentsAsync(CreateNotificationRequest request, CancellationToken cancellationToken)
    {
        var attachments = new List<Attachment>();
        if (request.Attachments == null)
            return Ok(attachments);

        foreach (var a in request.Attachments)
        {
            // 既没有 FileId 也没有 FilePath 的附件没有任何发送器装得上：接受它只会把失败推迟到后台逐收件人报出，
            // 而调用方早已拿到 200。与下面两道门同一判据 —— 拒绝要发生在创建这一刻。
            var violation = DescribeMissingSourceViolation(a)
                            ?? await AttachmentSourcePolicy.DescribeViolationAsync(a.FilePath, Options.Attachments, cancellationToken)
                            ?? await DescribeFileIdViolationAsync(a, cancellationToken);
            if (violation != null)
                return Fail<List<Attachment>>(violation, 400, ErrorCodes.NOTIFICATION_ERROR);

            attachments.Add(new Attachment
            {
                FileId = a.FileId,
                FileName = a.FileName,
                FilePath = a.FilePath,
                FileSize = a.FileSize,
                ContentType = a.ContentType
            });
        }

        return Ok(attachments);
    }

    /// <summary>附件必须至少带 <c>FileId</c> 或 <c>FilePath</c> 之一；都没有时返回可直接回给调用方的英文原因。</summary>
    private static string? DescribeMissingSourceViolation(FileInfoDto attachment)
    {
        var hasFileId = attachment.FileId is { } fileId && fileId != Guid.Empty;
        if (hasFileId || !string.IsNullOrWhiteSpace(attachment.FilePath))
            return null;

        return $"Attachment '{attachment.FileName}' has neither a FileId nor a FilePath, so nothing could ever be attached; "
               + "pass a stored file id, a path under Notification:Attachments:AllowedLocalRoots, or an allowed URL.";
    }

    /// <summary>只带 <c>FileId</c> 的附件允不允许；可以时返回 <see langword="null"/>，否则返回可直接回给调用方的英文原因。</summary>
    private async Task<string?> DescribeFileIdViolationAsync(FileInfoDto attachment, CancellationToken cancellationToken)
    {
        if (attachment.FileId is not { } fileId || fileId == Guid.Empty)
            return null;

        if (_fileContentReader == null)
        {
            return $"Attachment '{attachment.FileName}' references stored file {fileId}, but no IFileContentReader is registered: "
                   + "load Tnzi.Storage ([DependsOn(typeof(StorageModule))]) or pass a FilePath instead.";
        }

        // 没有当前用户 = 系统调用；有则必须证明这个人本来就读得到这份文件。
        var user = CurrentUser;
        if (user is not { IsAuthenticated: true })
            return null;

        if (_fileReadAccessProbe == null)
            return $"Attachment '{attachment.FileName}' references stored file {fileId}, but read access cannot be verified: no IFileReadAccessProbe is registered.";

        return await _fileReadAccessProbe.CanReadAsync(fileId, cancellationToken)
            ? null
            : $"Attachment '{attachment.FileName}' references stored file {fileId}, which the current user cannot read.";
    }

    /// <summary>
    /// 由一份创建请求构造出待落库的消息：校验收件人、渲染内容、定下服务商、过附件纪律，再拼实体。
    /// 失败时返回可直接回给调用方的英文原因。
    /// </summary>
    /// <remarks>
    /// 单条与批量两条创建路径共用这一份（批量那条此前自己拼实体，漏掉了 <c>ScheduledTime</c> 与定时状态）。
    /// </remarks>
    /// <param name="request">创建请求。</param>
    /// <param name="queuedAt">
    /// 排队发送时交给发送的那一刻（见 <see cref="QueuedAt"/>），写进 <c>ScheduledTime</c>；草稿与其它情形为 <see langword="null"/>。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<Result<Message>> BuildMessageAsync(CreateNotificationRequest request, DateTime? queuedAt, CancellationToken cancellationToken)
    {
        var validationError = ValidateRecipients(request.Recipients);
        if (validationError != null)
            return Fail<Message>(validationError, 400, ErrorCodes.NOTIFICATION_ERROR);

        var rendered = await RenderContentAsync(ContentSource.Of(request), cancellationToken);
        if (!rendered.Succeeded)
            return Fail<Message>(rendered.Message ?? "Notification content could not be rendered", rendered.Code ?? 400, ErrorCodes.NOTIFICATION_ERROR);

        var (subject, content, category) = rendered.Data;

        var providerKey = await ResolveProviderKeyAsync(request, category, cancellationToken);
        if (!providerKey.Succeeded)
            return Fail<Message>(providerKey.Message ?? "Notification provider could not be resolved", providerKey.Code ?? 400, ErrorCodes.NOTIFICATION_ERROR);

        var attachments = await BuildAttachmentsAsync(request, cancellationToken);
        if (!attachments.Succeeded)
            return Fail<Message>(attachments.Message ?? "Attachment source is not allowed", attachments.Code ?? 400, ErrorCodes.NOTIFICATION_ERROR);

        var notification = new Message
        {
            Type = request.Type,
            Subject = subject,
            Content = content,
            IsHtml = request.IsHtml,
            Priority = request.Priority,
            Status = request.ScheduledTime.HasValue ? NotificationStatus.Scheduled : NotificationStatus.Pending,
            SenderId = request.SenderId,
            Category = category,
            IsTransactional = request.IsTransactional,
            TemplateName = request.TemplateName,
            ProviderKey = providerKey.Data,
            ScheduledTime = request.ScheduledTime ?? queuedAt,
            RetryCount = 0,
            MaxRetryCount = request.MaxRetryCount > 0 ? request.MaxRetryCount : 3,
            TotalRecipientCount = request.Recipients.Count,
            SuccessCount = 0,
            FailureCount = 0
        };

        notification.Recipients = request.Recipients.Select(r => new Recipient
        {
            Address = r.Address,
            Name = r.Name,
            UserId = r.UserId,
            Status = NotificationStatus.Pending
        }).ToList();

        notification.Attachments = attachments.Data!;

        return Ok(notification);
    }

    /// <summary>发布「通知已创建」事件（没有事件总线时什么都不做）。</summary>
    private async Task PublishCreatedAsync(Message notification)
    {
        if (EventBus == null)
            return;

        await EventBus.PublishAsync(new NotificationCreatedEvent
        {
            MessageId = notification.Id,
            Type = notification.Type,
            RecipientCount = notification.TotalRecipientCount,
            Priority = notification.Priority,
            Category = notification.Category
        });
    }

    /// <summary>
    /// 排队发送（没定时、也不要求立即发送）的消息在落库时就写下「最早几点发」= 现在；其余返回 <see langword="null"/>。
    /// </summary>
    /// <remarks>
    /// ★★★ 排队只是把工作项写进<b>进程内</b>的有界队列，行停在 <c>Pending</c>。进程在队列排空之前退出，
    /// 这些行就永远停在那里：恢复扫描此前只看 <c>Sending</c> / <c>Scheduled</c>，而 <c>Pending</c> 同时也是
    /// 草稿（<c>CreateAsync</c>，只创建不发送）的状态，不能一概捎带发出去。<c>Pending</c> + <c>ScheduledTime</c>
    /// 就是「已经交给发送」的标记：派发恢复超过宽限仍见它停在 <c>Pending</c> 就接手（见
    /// <c>NotificationDispatchBackgroundService.ResumeOverdueQueuedAsync</c>），草稿不带它，永远不会被碰。
    /// 延迟重试写的是同一列（退避到期的时刻）。不加列，不需要迁移。
    /// </remarks>
    private static DateTime? QueuedAt(CreateNotificationRequest request)
        => request.ScheduledTime == null && !request.SendImmediately ? DateTime.UtcNow : null;

    /// <summary>
    /// 刚落库的消息接下来怎么发：定时到将来 → 延迟入队；要求立即发送 → 当场发；其余 → 入队。
    /// </summary>
    /// <remarks>
    /// ★ 顺序就是语义：定时优先于「立即发送」。单条与批量两条「创建并发送」共用这一份。
    /// 没有队列服务时定时消息不入队，行已是 <c>Scheduled</c> + <c>ScheduledTime</c>，到期后由派发恢复的扫描发出去。
    /// </remarks>
    private async Task<Result> StartDeliveryAsync(Guid messageId, CreateNotificationRequest request, CancellationToken cancellationToken)
    {
        if (request.ScheduledTime is { } scheduledTime && scheduledTime > DateTime.UtcNow)
        {
            if (_queueService != null)
            {
                await _queueService.EnqueueWithDelayAsync(
                    NotificationWorkItem.SendMessage(messageId, CurrentTenantId), scheduledTime - DateTime.UtcNow);
            }

            LogInformation("Notification scheduled: {NotificationId}, Type: {Type}, ScheduledTime: {ScheduledTime}",
                messageId, request.Type, scheduledTime);
            return Ok($"Notification scheduled for {scheduledTime:u}");
        }

        if (request.SendImmediately)
        {
            var sendResult = await SendAsync(messageId, cancellationToken);
            if (!sendResult.Succeeded)
                return Fail(sendResult.Message ?? "Failed to send notification", sendResult.Code ?? 400, sendResult.ErrorCode);
        }
        else
        {
            await QueueNotificationAsync(messageId, cancellationToken);
        }

        LogInformation("Notification created and queued: {NotificationId}, Type: {Type}", messageId, request.Type);
        return Ok("Notification created and queued successfully");
    }

    private async Task QueueNotificationAsync(Guid messageId, CancellationToken cancellationToken)
    {
        if (_queueService != null)
        {
            // 工作项自己带着租户（见 NotificationWorkItem）：队列在无租户的新作用域里执行，
            // 不带的话多租户下这条消息在那里查不到，SendAsync 回 404 而调用方早已拿到 200。
            await _queueService.EnqueueAsync(NotificationWorkItem.SendMessage(messageId, CurrentTenantId));
        }
        else
        {
            // 没有队列服务，直接发送
            await SendAsync(messageId, cancellationToken);
        }
    }


    #endregion
}
