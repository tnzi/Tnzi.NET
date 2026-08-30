using Message = Tnzi.Notification.Entities.Message;

namespace Tnzi.Notification.Services;

/// <summary>
/// 把一条已经定稿的消息投递给一个收件人：按 <see cref="Message.Type"/> 选渠道，
/// 套上单次投递的超时，返回这一次投递的结果。
/// </summary>
/// <remarks>
/// 与 <see cref="NotificationService"/> 分开，是因为两者管的是不同的事：那边管流程
/// （创建、入队、剔除退订与偏好关闭的收件人、落状态、重试），这里只回答「这条消息
/// 在这个渠道上怎么发出去」。新增一条渠道只动这一个文件。
/// </remarks>
internal sealed class RecipientChannelDispatcher
{
    private readonly IEmailSender _emailSender;
    private readonly ISmsSender _smsSender;
    private readonly IPushSender _pushSender;
    private readonly IFaxSender _faxSender;
    private readonly IOptionsMonitor<NotificationOptions> _optionsMonitor;
    private readonly INotificationOptOutService _optOutService;
    private readonly ILogger _logger;

    private NotificationOptions Options => _optionsMonitor.CurrentValue;

    public RecipientChannelDispatcher(
        IEmailSender emailSender,
        ISmsSender smsSender,
        IPushSender pushSender,
        IFaxSender faxSender,
        IOptionsMonitor<NotificationOptions> optionsMonitor,
        INotificationOptOutService optOutService,
        ILogger logger)
    {
        _emailSender = Check.NotNull(emailSender);
        _smsSender = Check.NotNull(smsSender);
        _pushSender = Check.NotNull(pushSender);
        _faxSender = Check.NotNull(faxSender);
        _optionsMonitor = Check.NotNull(optionsMonitor);
        _optOutService = Check.NotNull(optOutService);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 按渠道把 <paramref name="notification"/> 发给 <paramref name="recipient"/>，
    /// 单次投递不超过 <see cref="NotificationOptions.SendTimeoutSeconds"/>。
    /// </summary>
    public async Task<SendResult> DispatchAsync(Message notification, Recipient recipient, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Options.SendTimeoutSeconds));

        return notification.Type switch
        {
            NotificationType.Email => await SendEmailAsync(notification, recipient, cts.Token),
            NotificationType.Sms => await _smsSender.SendToAsync(recipient.Address, notification.Content, cts.Token),
            NotificationType.Push => await _pushSender.SendToAsync(recipient.Address, notification.Subject, notification.Content, cts.Token),
            NotificationType.Fax => await SendFaxAsync(notification, recipient, cts.Token),
            _ => new SendResult { Success = false, FailureReason = $"Unsupported notification type: {notification.Type}" }
        };
    }

    /// <summary>
    /// 把一条 <see cref="NotificationType.Fax"/> 消息发给一个收件人（其 <c>Address</c> 是传真号码）。
    /// </summary>
    /// <remarks>
    /// ★ <b><see cref="Message.Content"/> 不参与投递</b>：网关要求正文为空，所以这条消息的正文只是留档。
    /// 一份传真的全部内容就是那个 PDF —— 「附件恰好一个」在这里就要拦下，因为交给网关之后
    /// 多出来的那一份只会被安静丢掉（或者整份传真失败），而两种结果都不会有人告诉你。
    /// </remarks>
    private async Task<SendResult> SendFaxAsync(Message notification, Recipient recipient, CancellationToken cancellationToken)
    {
        var attachments = notification.Attachments;
        if (attachments is not { Count: 1 })
        {
            var count = attachments?.Count ?? 0;
            return SendResult.CreateFailure(
                $"A fax carries exactly one PDF attachment; this notification has {count}. " +
                "Combine the documents into a single PDF first (IPdfCombiner in Tnzi.Documents).");
        }

        var source = attachments.First();
        var document = new EmailAttachment
        {
            FileName = source.FileName,
            FilePath = source.FilePath,
            ContentType = source.ContentType
        };

        return await _faxSender.SendToAsync(recipient.Address, document, notification.Subject, cancellationToken);
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

        var headers = BuildUnsubscribeHeaders(notification, recipient);

        // ★ 没有信头就走原来那个方法，而不是一律走带信头的重载。这不是两份实现：
        // SendToWithHeadersAsync 的默认体本身就是「丢掉信头、调 SendToAsync」，所以
        // 「无信头 → SendToAsync」正是契约说的那件事。而对一个重写了该方法的自定义发送器来说，
        // 把不带信头的绝大多数投递也赶进那条较新、跑得较少的路径，是纯粹多出来的风险。
        return headers == null
            ? await _emailSender.SendToAsync(
                recipient.Address, recipient.Name, notification.Subject, notification.Content,
                notification.IsHtml, emailAttachments, cancellationToken)
            : await _emailSender.SendToWithHeadersAsync(
                recipient.Address, recipient.Name, notification.Subject, notification.Content,
                notification.IsHtml, emailAttachments, headers, cancellationToken);
    }

    /// <summary>
    /// 为这一次投递组装 RFC 8058 的退订信头。返回 <see langword="null"/> 表示这封信不带。
    /// </summary>
    /// <remarks>
    /// ★★ <b>信头是这个模块唯一能放退订链接的地方</b>：一条消息只渲染一次、多个收件人共用同一份
    /// 正文，而令牌是按地址签发的 —— 正文里放不进一条对每个收件人都正确的链接。信头在投递那一刻
    /// 逐封写入，天然按收件人。详见 <see cref="UnsubscribeHeaders"/>。
    /// <para>
    /// ★ 签发失败（最常见是 <c>Notification:OptOut:TokenSecret</c> 没配，它刻意抛异常）
    /// <b>不能连累这封信</b>：少一个退订按钮是可降级的，一封发不出去的通知不是。
    /// </para>
    /// </remarks>
    private Dictionary<string, string>? BuildUnsubscribeHeaders(Message notification, Recipient recipient)
    {
        var optOut = Options.OptOut;
        // ★ 这里只挡「这个部署没配退订落地页」这一个配置条件，为的是不去白签一个令牌。
        // 「事务性消息不带信头」那条规则**只写在 UnsubscribeHeaders.Build 里**：在这里再判一次
        // 是第二份拷贝，而这个仓库反复吃过同一条规则抄两遍然后漂开的亏。
        if (string.IsNullOrWhiteSpace(optOut.LandingUrl))
            return null;

        try
        {
            var token = _optOutService.CreateUnsubscribeToken(recipient.Address, notification.Type, notification.Category);
            return UnsubscribeHeaders.Build(notification.IsTransactional, optOut.LandingUrl, optOut.OneClickEndpoint, token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not mint an unsubscribe token for notification {NotificationId}; sending without the List-Unsubscribe header. Configure Notification:OptOut:TokenSecret to enable one-click unsubscribe.",
                notification.Id);
            return null;
        }
    }
}
