using Message = Tnzi.Notification.Entities.Message;

namespace Tnzi.Notification.Services;

/// <summary>
/// 通知服务实现（创建+发送编排）
/// </summary>
public class NotificationService : ApplicationService, INotificationService, IDisposable, IAsyncDisposable
{
    private readonly IRepository<Message, Guid> _notificationRepository;
    private readonly IEmailSender _emailSender;
    private readonly ISmsSender _smsSender;
    private readonly IPushSender _pushSender;
    private readonly INotificationQueueService? _queueService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly NotificationOptions _options;
    private readonly ITemplateStoreService? _templateStoreService;
    private readonly ILayoutStoreService? _layoutStoreService;
    private readonly ITemplateEngine? _templateEngine;
    private readonly SemaphoreSlim _semaphore;

    private new const string ModuleName = "Notification";

    public NotificationService(
        IRepository<Message, Guid> notificationRepository,
        IEmailSender emailSender,
        ISmsSender smsSender,
        IPushSender pushSender,
        IUnitOfWork unitOfWork,
        IOptions<NotificationOptions> options,
        IServiceProvider serviceProvider,
        INotificationQueueService? queueService = null,
        ITemplateStoreService? templateStoreService = null,
        ILayoutStoreService? layoutStoreService = null,
        ITemplateEngine? templateEngine = null)
        : base(serviceProvider)
    {
        _notificationRepository = Check.NotNull(notificationRepository);
        _emailSender = Check.NotNull(emailSender);
        _smsSender = Check.NotNull(smsSender);
        _pushSender = Check.NotNull(pushSender);
        _unitOfWork = Check.NotNull(unitOfWork);
        _options = Check.NotNull(options).Value;
        _queueService = queueService;
        _templateStoreService = templateStoreService;
        _layoutStoreService = layoutStoreService;
        _templateEngine = templateEngine;
        _semaphore = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
    }

    public async Task<Result<NotificationInfo>> CreateAndSendAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var createResult = await CreateAsync(request, cancellationToken);
        if (!createResult.Succeeded)
            return Fail<NotificationInfo>(createResult.Message ?? "Failed to create notification", createResult.Code ?? 400, createResult.ErrorCode);

        if (createResult.Data == null)
            return Fail<NotificationInfo>("Notification data is null after creation", 500, ErrorCodes.NOTIFICATION_ERROR);

        var notificationInfo = createResult.Data;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
        var timestamp = DateTime.UtcNow;

        var templateCache = new Dictionary<string, Tnzi.Template.Entities.Template>();
        var layoutCache = new Dictionary<string, Tnzi.Template.Entities.Layout?>();

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

                var renderResult = await RenderTemplateContentAsync(request, templateCache, layoutCache, cancellationToken);
                if (!renderResult.Succeeded)
                {
                    errors.Add($"Request at index {i}: {renderResult.Message}");
                    Logger.LogWarning("Failed to render template for request at index {Index}: {Error}", i, renderResult.Message);
                    continue;
                }

                var notification = new Message
                {
                    Type = request.Type,
                    Subject = renderResult.Subject,
                    Content = renderResult.Content,
                    IsHtml = request.IsHtml,
                    Priority = request.Priority,
                    Status = NotificationStatus.Pending,
                    SenderId = request.SenderId,
                    Category = renderResult.Category,
                    TemplateName = request.TemplateName,
                    CreationTime = timestamp,
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

        var renderResult = await RenderTemplateContentAsync(request, null, null, cancellationToken);
        if (!renderResult.Succeeded)
            return Fail<NotificationInfo>(renderResult.Message, 400, ErrorCodes.NOTIFICATION_ERROR);

        var notification = new Message
        {
            Type = request.Type,
            Subject = renderResult.Subject,
            Content = renderResult.Content,
            IsHtml = request.IsHtml,
            Priority = request.Priority,
            Status = NotificationStatus.Pending,
            SenderId = request.SenderId,
            Category = renderResult.Category,
            TemplateName = request.TemplateName,
            CreationTime = DateTime.UtcNow,
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
        var notification = await _notificationRepository
            .AsQueryable()
            .Include(n => n.Recipients)
            .Include(n => n.Attachments)
            .FirstOrDefaultAsync(n => n.Id == messageId, cancellationToken);

        if (notification == null)
            return Fail($"Notification {messageId} not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (notification.Status == NotificationStatus.Sent)
            return Fail($"Notification {messageId} has already been sent", 400, ErrorCodes.NOTIFICATION_ERROR);

        if (notification.Status == NotificationStatus.Cancelled)
            return Fail($"Notification {messageId} has been cancelled", 400, ErrorCodes.NOTIFICATION_ERROR);

        notification.Status = NotificationStatus.Sending;
        notification.RetryCount++;

        var pendingRecipients = notification.Recipients
            .Where(r => r.Status == NotificationStatus.Pending || r.Status == NotificationStatus.Failed)
            .ToList();

        if (pendingRecipients.Count == 0)
        {
            notification.Status = NotificationStatus.Sent;
            notification.SentTime = DateTime.UtcNow;
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
            return Ok("No pending recipients to send to");
        }

        var successCount = 0;
        var failureCount = 0;
        string? lastError = null;

        foreach (var recipient in pendingRecipients)
        {
            await _semaphore.WaitAsync(cancellationToken);
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
                _semaphore.Release();
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
        var notification = await _notificationRepository.GetAsync(messageId, cancellationToken);
        if (notification == null)
            return Fail($"Notification {messageId} not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (notification.Status == NotificationStatus.Sent || notification.Status == NotificationStatus.PartiallySent)
            return Fail($"Notification {messageId} has already been sent and cannot be cancelled", 400, ErrorCodes.NOTIFICATION_ERROR);

        if (notification.Status == NotificationStatus.Cancelled)
            return Ok("Notification is already cancelled");

        notification.Status = NotificationStatus.Cancelled;
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        LogInformation("Notification cancelled: {NotificationId}", messageId);
        return Ok("Notification cancelled successfully");
    }

    #region Private Methods

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

    private async Task<TemplateRenderResult> RenderTemplateContentAsync(
        CreateNotificationRequest request,
        Dictionary<string, Tnzi.Template.Entities.Template>? templateCache,
        Dictionary<string, Tnzi.Template.Entities.Layout?>? layoutCache,
        CancellationToken cancellationToken)
    {
        // 如果没有模板名称，直接使用请求中的内容
        if (string.IsNullOrWhiteSpace(request.TemplateName))
        {
            return TemplateRenderResult.Ok(
                request.Subject,
                request.Content,
                request.Category ?? "General");
        }

        // 没有模板服务时使用原始内容
        if (_templateStoreService == null || _templateEngine == null)
        {
            Logger.LogWarning("Template services not available, using raw content for template '{TemplateName}'", request.TemplateName);
            return TemplateRenderResult.Ok(
                request.Subject,
                request.Content,
                request.Category ?? "General");
        }

        try
        {
            // 查找模板（支持缓存）
            Tnzi.Template.Entities.Template? template;
            if (templateCache != null && templateCache.TryGetValue(request.TemplateName, out var cachedTemplate))
            {
                template = cachedTemplate;
            }
            else
            {
                var templateResult = await _templateStoreService.GetTemplateAsync(request.TemplateName, ModuleName, null, cancellationToken);
                template = templateResult.Succeeded ? templateResult.Data : null;
                if (template != null)
                    templateCache?.TryAdd(request.TemplateName, template);
            }

            if (template == null)
                return TemplateRenderResult.Fail($"Template '{request.TemplateName}' not found");

            // 渲染模板内容
            var variables = request.TemplateVariables ?? new Dictionary<string, object>();
            var content = await _templateEngine.RenderAsync(template.ContentTemplate, variables);
            var subject = !string.IsNullOrWhiteSpace(template.SubjectTemplate)
                ? await _templateEngine.RenderAsync(template.SubjectTemplate, variables)
                : request.Subject;

            // 如果指定了布局，渲染布局
            var layoutName = request.LayoutName ?? template.DefaultLayoutName;
            if (!string.IsNullOrWhiteSpace(layoutName) && _layoutStoreService != null)
            {
                Tnzi.Template.Entities.Layout? layout;
                if (layoutCache != null && layoutCache.TryGetValue(layoutName, out var cachedLayout))
                {
                    layout = cachedLayout;
                }
                else
                {
                    var layoutResult = await _layoutStoreService.GetLayoutAsync(layoutName, ModuleName, null, cancellationToken);
                    layout = layoutResult.Succeeded ? layoutResult.Data : null;
                    layoutCache?.TryAdd(layoutName, layout);
                }

                if (layout?.LayoutContent != null)
                {
                    var layoutVariables = new Dictionary<string, object>(variables)
                    {
                        ["Body"] = content
                    };
                    content = await _templateEngine.RenderAsync(layout.LayoutContent, layoutVariables);
                }
            }

            return TemplateRenderResult.Ok(
                subject,
                content,
                template.Category ?? request.Category ?? "General");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to render template '{TemplateName}'", request.TemplateName);
            return TemplateRenderResult.Fail($"Failed to render template: {ex.Message}");
        }
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

    private async Task<SendResult> SendToRecipientAsync(Message notification, Recipient recipient, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.SendTimeoutSeconds));

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

    #region IDisposable

    public void Dispose()
    {
        _semaphore.Dispose();
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    #endregion

    /// <summary>
    /// 模板渲染结果
    /// </summary>
    private class TemplateRenderResult
    {
        public bool Succeeded { get; init; }
        public string Message { get; init; } = string.Empty;
        public string Subject { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public string Category { get; init; } = "General";

        public static TemplateRenderResult Ok(string subject, string content, string category)
            => new() { Succeeded = true, Subject = subject, Content = content, Category = category };

        public static TemplateRenderResult Fail(string message)
            => new() { Succeeded = false, Message = message };
    }
}
