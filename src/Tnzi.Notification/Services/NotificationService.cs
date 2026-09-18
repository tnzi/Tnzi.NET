using Message = Tnzi.Notification.Entities.Message;

namespace Tnzi.Notification.Services;

/// <summary>
/// 通知服务实现（创建+发送编排）
/// </summary>
public class NotificationService : ApplicationService, INotificationService
{
    private readonly IRepository<Message, Guid> _notificationRepository;
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
        INotificationQueueService? queueService = null,
        ITemplateRenderService? templateRenderService = null,
        ICurrentTenant? currentTenant = null,
        IFileContentReader? fileContentReader = null,
        IFileReadAccessProbe? fileReadAccessProbe = null)
        : base(serviceProvider)
    {
        _notificationRepository = Check.NotNull(notificationRepository);
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
            _notificationRepository, _unitOfWork, _optOutService, _preferenceService, Logger);
        _queueService = queueService;
        _templateRenderService = templateRenderService;
        _currentTenant = currentTenant;

        EnsureSemaphoreInitialized(Options.MaxConcurrency);
    }

    public async Task<Result<NotificationInfo>> CreateAndSendAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var createResult = await CreateAsync(request, cancellationToken);
        if (!createResult.Succeeded)
            return Fail<NotificationInfo>(createResult.Message ?? "Failed to create notification", createResult.Code ?? 400, createResult.ErrorCode);

        if (createResult.Data == null)
            return Fail<NotificationInfo>("Notification data is null after creation", 500, ErrorCodes.NOTIFICATION_ERROR);

        var notificationInfo = createResult.Data;

        // Handle scheduled notifications: defer sending until ScheduledTime
        if (request.ScheduledTime.HasValue && request.ScheduledTime.Value > DateTime.UtcNow)
        {
            var delay = request.ScheduledTime.Value - DateTime.UtcNow;
            if (_queueService != null)
            {
                await _queueService.EnqueueWithDelayAsync(
                    NotificationWorkItem.SendMessage(notificationInfo.Id, CurrentTenantId), delay);
            }

            LogInformation("Notification scheduled: {NotificationId}, Type: {Type}, ScheduledTime: {ScheduledTime}",
                notificationInfo.Id, notificationInfo.Type, request.ScheduledTime.Value);
            return Ok(notificationInfo, $"Notification scheduled for {request.ScheduledTime.Value:u}");
        }

        if (request.SendImmediately)
        {
            var sendResult = await SendAsync(notificationInfo.Id, cancellationToken);
            if (!sendResult.Succeeded)
                return Fail<NotificationInfo>(sendResult.Message ?? "Failed to send notification", sendResult.Code ?? 400, sendResult.ErrorCode);
        }
        else
        {
            await QueueNotificationAsync(notificationInfo.Id, cancellationToken);
        }

        LogInformation("Notification created and queued: {NotificationId}, Type: {Type}", notificationInfo.Id, notificationInfo.Type);
        return Ok(notificationInfo, "Notification created and queued successfully");
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
                var validationError = ValidateRecipients(request.Recipients);
                if (validationError != null)
                {
                    errors.Add($"Request at index {i}: {validationError}");
                    continue;
                }

                var rendered = await RenderContentAsync(request, cancellationToken);
                if (!rendered.Succeeded)
                {
                    errors.Add($"Request at index {i}: {rendered.Message}");
                    continue;
                }

                var (subject, content, category) = rendered.Data;

                var providerKey = await ResolveProviderKeyAsync(request, category, cancellationToken);
                if (!providerKey.Succeeded)
                {
                    errors.Add($"Request at index {i}: {providerKey.Message}");
                    continue;
                }

                var attachments = await BuildAttachmentsAsync(request, cancellationToken);
                if (!attachments.Succeeded)
                {
                    errors.Add($"Request at index {i}: {attachments.Message}");
                    continue;
                }

                var notification = new Message
                {
                    Type = request.Type,
                    Subject = subject,
                    Content = content,
                    IsHtml = request.IsHtml,
                    Priority = request.Priority,
                    Status = NotificationStatus.Pending,
                    SenderId = request.SenderId,
                    Category = category,
                    IsTransactional = request.IsTransactional,
                    TemplateName = request.TemplateName,
                    ProviderKey = providerKey.Data,
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

                messageRequestMap.Add((notification, request));
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

        foreach (var (message, request) in messageRequestMap)
        {
            if (request.SendImmediately)
            {
                var sendResult = await SendAsync(message.Id, cancellationToken);
                if (!sendResult.Succeeded)
                    Logger.LogWarning("Failed to send notification {NotificationId} immediately: {Error}", message.Id, sendResult.Message);
            }
            else
            {
                await QueueNotificationAsync(message.Id, cancellationToken);
            }
        }

        var successMessage = errors.Count > 0
            ? $"Processed {messages.Count} messages successfully, {errors.Count} failed: {string.Join("; ", errors)}"
            : $"Processed {messages.Count} messages successfully";

        LogInformation("Batch created and queued {Count} notifications. Errors: {ErrorCount}", messages.Count, errors.Count);
        var notificationInfos = RecipientAddressMask.Apply(messages.MapToList<NotificationInfo>());
        return Ok((IEnumerable<NotificationInfo>)notificationInfos, successMessage);
    }

    public async Task<Result<NotificationInfo>> CreateAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var validationError = ValidateRecipients(request.Recipients);
        if (validationError != null)
            return Fail<NotificationInfo>(validationError, 400, ErrorCodes.NOTIFICATION_ERROR);

        var rendered = await RenderContentAsync(request, cancellationToken);
        if (!rendered.Succeeded)
            return Fail<NotificationInfo>(rendered.Message ?? "Notification content could not be rendered", rendered.Code ?? 400, ErrorCodes.NOTIFICATION_ERROR);

        var (subject, content, category) = rendered.Data;

        var providerKey = await ResolveProviderKeyAsync(request, category, cancellationToken);
        if (!providerKey.Succeeded)
            return Fail<NotificationInfo>(providerKey.Message ?? "Notification provider could not be resolved", providerKey.Code ?? 400, ErrorCodes.NOTIFICATION_ERROR);

        var attachments = await BuildAttachmentsAsync(request, cancellationToken);
        if (!attachments.Succeeded)
            return Fail<NotificationInfo>(attachments.Message ?? "Attachment source is not allowed", attachments.Code ?? 400, ErrorCodes.NOTIFICATION_ERROR);

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
            ScheduledTime = request.ScheduledTime,
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

        await _notificationRepository.InsertAsync(notification, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 发布通知创建事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new NotificationCreatedEvent
            {
                MessageId = notification.Id,
                Type = notification.Type,
                RecipientCount = notification.TotalRecipientCount,
                Priority = notification.Priority,
                Category = notification.Category
            });
        }

        LogInformation("Notification created: {NotificationId}, Type: {Type}, Recipients: {Count}",
            notification.Id, notification.Type, notification.TotalRecipientCount);

        // 推送渠道的「地址」是设备令牌，对外一律掩码（见 RecipientAddressMask）。
        return Ok(RecipientAddressMask.Apply(notification.MapTo<NotificationInfo>()));
    }

    public async Task<Result> SendAsync(Guid messageId, CancellationToken cancellationToken = default)
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

        notification.Status = NotificationStatus.Sending;
        notification.RetryCount++;

        // ★★ 这一句必须**立刻落库**，不能等到方法末尾那次 SaveChanges。
        // 「正在发送」在这一刻就已经是事实，而库里的行在整个收件人循环期间（一次千人群发
        // 可能几十分钟）都还写着 Scheduled / Pending —— 于是：
        //  ① 到期扫描的条件认领（`WHERE Status = Scheduled`）会**合法地**抢到一条正在发送的
        //     消息，第二次 SendAsync 看到全部收件人仍是 Pending，把已经发出去的那些再发一遍。
        //     那正是本轮一直在消灭的形态，而它是「宽限期能挡住本进程定时器」这个**错误前提**
        //     的后果：定时器到点只是**入队**，队列单读者串行执行，真正开发的时刻取决于积压。
        //  ② 进程在循环中途退出时，行仍停在发送前的状态，于是卡住批次那一遍也扫不到它 ——
        //     两道恢复都够不着，消息永远停在那里。
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
            //   ① 有人被延后 → 这条消息还有下文，状态回到 Scheduled 等到期扫描接手，
            //      **不能**盖 SentTime（那会让一条还没送到的消息显示成已发送）。
            //   ② 全员被拦   → 到此为止，Cancelled。
            //   ③ 本来就没有待发收件人 → 维持原有语义，Sent。
            notification.Status = deferredCount > 0
                ? NotificationStatus.Scheduled
                : everyoneFilteredOut ? NotificationStatus.Cancelled : NotificationStatus.Sent;

            if (deferredCount == 0)
                notification.SentTime = DateTime.UtcNow;

            await _notificationRepository.UpdateAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (deferredCount > 0)
                return Ok($"{deferredCount} recipient(s) are inside their quiet hours; delivery was deferred, not dropped.");

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
        var stillDeferred = notification.Recipients.Any(
            r => r.Status == NotificationStatus.Scheduled && r.DeferredUntil != null);

        if (stillDeferred)
        {
            notification.Status = NotificationStatus.Scheduled;
            notification.FailureReason = notification.FailureCount > 0 ? lastError : notification.FailureReason;
        }
        else if (notification.FailureCount == 0)
        {
            notification.Status = NotificationStatus.Sent;
            notification.SentTime = DateTime.UtcNow;
        }
        else if (notification.SuccessCount > 0)
        {
            notification.Status = NotificationStatus.PartiallySent;
            notification.SentTime = DateTime.UtcNow;
            notification.FailureReason = lastError;
        }
        else
        {
            notification.Status = NotificationStatus.Failed;
            notification.FailureReason = lastError;
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 发布事件
        if (EventBus != null)
        {
            if (notification.Status == NotificationStatus.Failed)
            {
                await EventBus.PublishAsync(new NotificationFailedEvent
                {
                    MessageId = notification.Id,
                    Type = notification.Type,
                    FailureReason = notification.FailureReason ?? "Unknown error",
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

        return notification.Status == NotificationStatus.Failed
            ? Fail($"Failed to send notification: {lastError}", 500, ErrorCodes.NOTIFICATION_ERROR)
            : Ok("Notification sent successfully");
    }

    public async Task<Result> CancelAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.AsQueryable(withTracking: true)
            .Include(m => m.Recipients)
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (notification == null)
            return Fail($"Notification {messageId} not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (notification.Status == NotificationStatus.Sent || notification.Status == NotificationStatus.PartiallySent)
            return Fail($"Notification {messageId} has already been sent and cannot be cancelled", 400, ErrorCodes.NOTIFICATION_ERROR);

        if (notification.Status == NotificationStatus.Cancelled)
            return Ok("Notification is already cancelled");

        notification.Status = NotificationStatus.Cancelled;

        // Cascade: update pending/scheduled recipients to cancelled
        foreach (var recipient in notification.Recipients.Where(r =>
            r.Status == NotificationStatus.Pending || r.Status == NotificationStatus.Scheduled))
        {
            recipient.Status = NotificationStatus.Cancelled;
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        LogInformation("Notification cancelled: {NotificationId}", messageId);
        return Ok("Notification cancelled successfully");
    }

    public async Task<Result<int>> BatchCancelAsync(List<Guid> ids, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(ids);

        var notifications = await _notificationRepository
            .AsQueryable(withTracking: true)
            .Include(n => n.Recipients)
            .Where(n => ids.Contains(n.Id) && (n.Status == NotificationStatus.Pending || n.Status == NotificationStatus.Scheduled))
            .ToListAsync(cancellationToken);

        if (notifications.Count == 0)
            return Ok(0, "No cancellable notifications found");

        foreach (var notification in notifications)
        {
            notification.Status = NotificationStatus.Cancelled;

            // Cascade: update pending/scheduled recipients to cancelled
            foreach (var recipient in notification.Recipients.Where(r =>
                r.Status == NotificationStatus.Pending || r.Status == NotificationStatus.Scheduled))
            {
                recipient.Status = NotificationStatus.Cancelled;
            }
        }

        await _notificationRepository.UpdateManyAsync(notifications, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        LogInformation("Batch cancelled {Count} notifications", notifications.Count);
        return Ok(notifications.Count, $"{notifications.Count} notifications cancelled");
    }

    public async Task<Result<NotificationPreviewDto>> PreviewAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var validationError = ValidateRecipients(request.Recipients);
        if (validationError != null)
            return Fail<NotificationPreviewDto>(validationError, 400, ErrorCodes.NOTIFICATION_ERROR);

        var rendered = await RenderContentAsync(request, cancellationToken);
        if (!rendered.Succeeded)
            return Fail<NotificationPreviewDto>(rendered.Message ?? "Notification content could not be rendered", rendered.Code ?? 400, ErrorCodes.NOTIFICATION_ERROR);

        var (subject, content, category) = rendered.Data;

        return Ok(new NotificationPreviewDto
        {
            Subject = subject,
            Content = content,
            IsHtml = request.IsHtml,
            Category = category,
            RecipientCount = request.Recipients.Count,
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

        // 重发同样要过四道过滤：上次失败之后对方可能已经退订，而这条路径绕开 SendAsync。
        (failedRecipients, _) = await _eligibility.FilterAsync(notification, failedRecipients, cancellationToken);

        if (failedRecipients.Count == 0)
            return Ok(0, "No failed recipients to resend to");

        var successCount = 0;
        // 与 SendAsync 同一条理由：整批只在末尾保存一次时，中途崩溃会把已经重发出去的
        // 那些人退回 Failed，下一次「重发失败项」再发一遍。
        var progress = new SendProgress(_notificationRepository, _unitOfWork, notification);

        foreach (var recipient in failedRecipients)
        {
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

        if (notification.FailureCount == 0)
        {
            notification.Status = NotificationStatus.Sent;
        }
        else if (notification.SuccessCount > 0)
        {
            notification.Status = NotificationStatus.PartiallySent;
        }

        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        LogInformation("Resent notification {NotificationId} to {Count} failed recipients, {Success} succeeded",
            messageId, failedRecipients.Count, successCount);

        return Ok(successCount, $"{successCount}/{failedRecipients.Count} recipients resent successfully");
    }

    #region Private Methods


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

        if (!_providers.IsRegistered(request.Type, selectedKey))
            return Fail<string?>(
                $"{_providerSelector.GetType().Name} selected a provider that is not registered. "
                + NotificationProviderProfiles.NotRegisteredMessage(request.Type, selectedKey),
                500, ErrorCodes.NOTIFICATION_ERROR);

        return Ok<string?>(selectedKey);
    }

    /// <summary>渲染后的消息内容：主题、正文与消息分类。</summary>
    private readonly record struct RenderedContent(string Subject, string Content, string Category);

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
    private async Task<Result<RenderedContent>> RenderContentAsync(CreateNotificationRequest request, CancellationToken cancellationToken)
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
    private static TemplateOutputKind? ResolveOutputKind(CreateNotificationRequest request) => request.Type switch
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
