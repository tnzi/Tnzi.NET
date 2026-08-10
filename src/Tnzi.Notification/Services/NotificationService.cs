using Message = Tnzi.Notification.Entities.Message;

namespace Tnzi.Notification.Services;

/// <summary>
/// 通知服务实现（创建+发送编排）
/// </summary>
public class NotificationService : ApplicationService, INotificationService
{
    private readonly IRepository<Message, Guid> _notificationRepository;
    private readonly IEmailSender _emailSender;
    private readonly ISmsSender _smsSender;
    private readonly IPushSender _pushSender;
    private readonly INotificationQueueService? _queueService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOptionsMonitor<NotificationOptions> _optionsMonitor;
    private readonly INotificationOptOutService _optOutService;
    private readonly INotificationPreferenceService _preferenceService;
    private readonly ITemplateRenderService? _templateRenderService;

    // 静态信号量，确保跨请求的并发控制真正生效
    private static SemaphoreSlim? _sendSemaphore;
    private static readonly object _semaphoreLock = new();

    private NotificationOptions Options => _optionsMonitor.CurrentValue;

    public NotificationService(
        IRepository<Message, Guid> notificationRepository,
        IEmailSender emailSender,
        ISmsSender smsSender,
        IPushSender pushSender,
        IUnitOfWork unitOfWork,
        IOptionsMonitor<NotificationOptions> optionsMonitor,
        IServiceProvider serviceProvider,
        INotificationOptOutService optOutService,
        INotificationPreferenceService preferenceService,
        INotificationQueueService? queueService = null,
        ITemplateRenderService? templateRenderService = null)
        : base(serviceProvider)
    {
        _notificationRepository = Check.NotNull(notificationRepository);
        _emailSender = Check.NotNull(emailSender);
        _smsSender = Check.NotNull(smsSender);
        _pushSender = Check.NotNull(pushSender);
        _unitOfWork = Check.NotNull(unitOfWork);
        _optionsMonitor = Check.NotNull(optionsMonitor);
        // 必需而非可选：本模块自己无条件注册它，缺了就该在容器里立刻炸，
        // 而不是让退订在运行时静默失效 —— 后者恰恰是这条修复要终结的形态。
        _optOutService = Check.NotNull(optOutService);
        // 同上：本模块自己无条件注册它，缺了就该在容器里立刻炸，
        // 而不是让「用户关掉的通知照发」在运行时静默成立。
        _preferenceService = Check.NotNull(preferenceService);
        _queueService = queueService;
        _templateRenderService = templateRenderService;

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
                    (sp, ct) =>
                    {
                        var svc = sp.GetRequiredService<INotificationService>();
                        return svc.SendAsync(notificationInfo.Id, ct);
                    },
                    delay);
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

                var (subject, content, category) = await RenderContentAsync(request, cancellationToken);

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

                notification.Attachments = request.Attachments?.Select(a => new Attachment
                {
                    FileId = a.FileId,
                    FileName = a.FileName,
                    FilePath = a.FilePath,
                    FileSize = a.FileSize,
                    ContentType = a.ContentType
                }).ToList() ?? new List<Attachment>();

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
        var notificationInfos = messages.MapToList<NotificationInfo>();
        return Ok((IEnumerable<NotificationInfo>)notificationInfos, successMessage);
    }

    public async Task<Result<NotificationInfo>> CreateAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var validationError = ValidateRecipients(request.Recipients);
        if (validationError != null)
            return Fail<NotificationInfo>(validationError, 400, ErrorCodes.NOTIFICATION_ERROR);

        var (subject, content, category) = await RenderContentAsync(request, cancellationToken);

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

        notification.Attachments = request.Attachments?.Select(a => new Attachment
        {
            FileId = a.FileId,
            FileName = a.FileName,
            FilePath = a.FilePath,
            FileSize = a.FileSize,
            ContentType = a.ContentType
        }).ToList() ?? new List<Attachment>();

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

        return Ok(notification.MapTo<NotificationInfo>());
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

        var pendingRecipients = notification.Recipients
            .Where(r => r.Status == NotificationStatus.Pending || r.Status == NotificationStatus.Failed)
            .ToList();

        // 退订与偏好都在发送那一刻判定（见两个 Exclude* 方法）：定时与排队的消息可能
        // 几天后才发出去，这两件事随时可能发生在这中间。
        var candidateCount = pendingRecipients.Count;
        pendingRecipients = await ExcludeOptedOutAsync(notification, pendingRecipients, cancellationToken);
        pendingRecipients = await ExcludePreferenceDisabledAsync(notification, pendingRecipients, cancellationToken);
        var everyoneOptedOut = pendingRecipients.Count == 0 && candidateCount > 0;

        if (pendingRecipients.Count == 0)
        {
            // ★ 全员退订时不能报 Sent —— 一封谁也没收到的消息在列表里显示"已发送"，
            // 正是这轮修复要终结的那种会被当真的谎。原有的"本来就无待发收件人"语义不变。
            notification.Status = everyoneOptedOut ? NotificationStatus.Cancelled : NotificationStatus.Sent;
            notification.SentTime = DateTime.UtcNow;
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
            return everyoneOptedOut
                ? Ok("Every recipient has opted out; nothing was sent")
                : Ok("No pending recipients to send to");
        }

        var successCount = 0;
        var failureCount = 0;
        string? lastError = null;

        foreach (var recipient in pendingRecipients)
        {
            await _sendSemaphore!.WaitAsync(cancellationToken);
            try
            {
                var sendResult = await SendToRecipientAsync(notification, recipient, cancellationToken);
                if (sendResult.Success)
                {
                    recipient.Status = NotificationStatus.Sent;
                    recipient.SentTime = DateTime.UtcNow;
                    recipient.ExternalMessageId = sendResult.ExternalMessageId;
                    successCount++;
                }
                else
                {
                    recipient.Status = NotificationStatus.Failed;
                    recipient.FailureReason = sendResult.FailureReason;
                    lastError = sendResult.FailureReason;
                    failureCount++;
                }
            }
            catch (Exception ex)
            {
                recipient.Status = NotificationStatus.Failed;
                recipient.FailureReason = ex.Message;
                lastError = ex.Message;
                failureCount++;
                Logger.LogError(ex, "Error sending notification {NotificationId} to {Address}", messageId, recipient.Address);
            }
            finally
            {
                _sendSemaphore!.Release();
            }
        }

        // 更新消息统计
        notification.SuccessCount = notification.Recipients.Count(r => r.Status == NotificationStatus.Sent);
        notification.FailureCount = notification.Recipients.Count(r => r.Status == NotificationStatus.Failed);

        if (notification.FailureCount == 0)
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

        var (subject, content, category) = await RenderContentAsync(request, cancellationToken);

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

        // 重发同样要过退订：上次失败之后对方可能已经退订，而这条路径绕开 SendAsync。
        failedRecipients = await ExcludeOptedOutAsync(notification, failedRecipients, cancellationToken);
        failedRecipients = await ExcludePreferenceDisabledAsync(notification, failedRecipients, cancellationToken);

        if (failedRecipients.Count == 0)
            return Ok(0, "No failed recipients to resend to");

        var successCount = 0;
        foreach (var recipient in failedRecipients)
        {
            recipient.Status = NotificationStatus.Pending;
            recipient.FailureReason = null;

            await _sendSemaphore!.WaitAsync(cancellationToken);
            try
            {
                var sendResult = await SendToRecipientAsync(notification, recipient, cancellationToken);
                if (sendResult.Success)
                {
                    recipient.Status = NotificationStatus.Sent;
                    recipient.SentTime = DateTime.UtcNow;
                    recipient.ExternalMessageId = sendResult.ExternalMessageId;
                    successCount++;
                }
                else
                {
                    recipient.Status = NotificationStatus.Failed;
                    recipient.FailureReason = sendResult.FailureReason;
                }
            }
            catch (Exception ex)
            {
                recipient.Status = NotificationStatus.Failed;
                recipient.FailureReason = ex.Message;
                Logger.LogError(ex, "Error resending notification {NotificationId} to {Address}", messageId, recipient.Address);
            }
            finally
            {
                _sendSemaphore!.Release();
            }
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
    /// 渲染通知内容（使用 ITemplateRenderService 或直接使用请求内容）
    /// </summary>
    private async Task<(string Subject, string Content, string Category)> RenderContentAsync(CreateNotificationRequest request, CancellationToken cancellationToken)
    {
        // 没有模板名称，直接使用请求中的内容
        if (string.IsNullOrWhiteSpace(request.TemplateName))
            return (request.Subject, request.Content, request.Category ?? "General");

        // 没有模板渲染服务，使用原始内容
        if (_templateRenderService == null)
        {
            Logger.LogWarning("ITemplateRenderService not available, using raw content for template '{TemplateName}'", request.TemplateName);
            return (request.Subject, request.Content, request.Category ?? "General");
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

        // 使用 ITemplateRenderService 一站式渲染
        var renderResult = await _templateRenderService.RenderByNameAsync(
            request.TemplateName,
            "Notification",
            request.TemplateVariables,
            templateCategory,
            request.LayoutName,
            cancellationToken);

        if (!renderResult.Succeeded)
        {
            Logger.LogWarning("Template rendering failed for '{TemplateName}': {Error}. Using raw content.", request.TemplateName, renderResult.Message);
            return (request.Subject, request.Content, request.Category ?? "General");
        }

        var rendered = renderResult.Data!;
        var subject = !string.IsNullOrWhiteSpace(rendered.Subject) ? rendered.Subject : request.Subject;
        return (subject, rendered.Content, request.Category ?? "General");
    }

    private async Task QueueNotificationAsync(Guid messageId, CancellationToken cancellationToken)
    {
        if (_queueService != null)
        {
            await _queueService.EnqueueAsync((sp, ct) =>
            {
                var svc = sp.GetRequiredService<INotificationService>();
                return svc.SendAsync(messageId, ct);
            });
        }
        else
        {
            // 没有队列服务，直接发送
            await SendAsync(messageId, cancellationToken);
        }
    }

    /// <summary>
    /// 从待发列表里剔除已退订的收件人，并把他们就地标记为
    /// <see cref="NotificationStatus.Cancelled"/>。返回仍应当发送的那些。
    /// </summary>
    /// <remarks>
    /// 判定规则在 <see cref="OptOutRecipientFilter"/>（纯函数，含"为什么这么定"的完整说明）；
    /// 这里只负责问一次退订名单并把结果套上去。
    /// </remarks>
    private async Task<List<Recipient>> ExcludeOptedOutAsync(
        Message notification, List<Recipient> candidates, CancellationToken cancellationToken)
    {
        if (!OptOutRecipientFilter.ShouldConsultOptOutList(notification, candidates.Count))
            return candidates;

        var allowed = await _optOutService.FilterAllowedAsync(
            candidates.Select(r => r.Address),
            notification.Type,
            notification.Category,
            cancellationToken);

        var remaining = OptOutRecipientFilter.Apply(candidates, allowed);
        if (remaining.Count != candidates.Count)
        {
            Logger.LogInformation(
                "Notification {NotificationId}: skipped {SkippedCount} of {TotalCount} recipient(s) that opted out",
                notification.Id, candidates.Count - remaining.Count, candidates.Count);

            // ★ 就地落库，不指望调用方。「因退订而未发」是要拿去交差的记录，
            // 而两条调用路径都有「过滤完就什么都不剩 → 提前 return」的分支：
            // SendAsync 那条只 UpdateAsync 不 SaveChanges，ResendToFailedRecipientsAsync
            // 那条**两样都没有** —— 标记就只活在被跟踪的实体里，随请求一起消失。
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return remaining;
    }

    /// <summary>
    /// 把「该渠道已被本人在偏好里关掉」的收件人择出去，就地标记为
    /// <see cref="NotificationStatus.Cancelled"/>。返回仍应当发送的那些。
    /// </summary>
    /// <remarks>
    /// 判定规则在 <see cref="PreferenceRecipientFilter"/>（纯函数，含"为什么这么定"的完整说明）；
    /// 这里只负责问一次偏好表并把结果套上去。
    /// <para>
    /// ★ <b>与退订并列而不是二选一</b>：退订按地址（收件人未必是注册用户），
    /// 偏好按人（同一个人在多个渠道上的开关）—— 两者管的是不同的东西，
    /// 任一说「别发」就不发。
    /// </para>
    /// </remarks>
    private async Task<List<Recipient>> ExcludePreferenceDisabledAsync(
        Message notification, List<Recipient> candidates, CancellationToken cancellationToken)
    {
        if (!PreferenceRecipientFilter.ShouldConsultPreferences(notification, candidates))
            return candidates;

        var enabled = await _preferenceService.FilterEnabledUsersAsync(
            PreferenceRecipientFilter.UserIdsToCheck(candidates),
            notification.Type,
            notification.Category,
            cancellationToken);

        var remaining = PreferenceRecipientFilter.Apply(candidates, enabled);
        if (remaining.Count != candidates.Count)
        {
            Logger.LogInformation(
                "Notification {NotificationId}: skipped {SkippedCount} of {TotalCount} recipient(s) who disabled this channel in their preferences",
                notification.Id, candidates.Count - remaining.Count, candidates.Count);

            // ★ 就地落库，理由与 ExcludeOptedOutAsync 逐字相同：两条调用路径都有
            // 「过滤完就什么都不剩 → 提前 return」的分支，不在这里落库标记就只活在
            // 被跟踪的实体里、随请求一起消失。
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return remaining;
    }

    private async Task<SendResult> SendToRecipientAsync(Message notification, Recipient recipient, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Options.SendTimeoutSeconds));

        return notification.Type switch
        {
            NotificationType.Email => await SendEmailAsync(notification, recipient, cts.Token),
            NotificationType.Sms => await _smsSender.SendToAsync(recipient.Address, notification.Content, cts.Token),
            NotificationType.Push => await _pushSender.SendToAsync(recipient.Address, notification.Subject, notification.Content, cts.Token),
            _ => new SendResult { Success = false, FailureReason = $"Unsupported notification type: {notification.Type}" }
        };
    }

    private async Task<SendResult> SendEmailAsync(Message notification, Recipient recipient, CancellationToken cancellationToken)
    {
        List<EmailAttachment>? emailAttachments = null;
        if (notification.Attachments?.Count > 0)
        {
            emailAttachments = notification.Attachments.Select(a => new EmailAttachment
            {
                FileName = a.FileName,
                FilePath = a.FilePath,
                ContentType = a.ContentType
            }).ToList();
        }

        return await _emailSender.SendToAsync(
            recipient.Address,
            recipient.Name,
            notification.Subject,
            notification.Content,
            notification.IsHtml,
            emailAttachments,
            cancellationToken);
    }

    #endregion
}
