using MailKit.Security;
using MimeKit;
using ContentType = MimeKit.ContentType;

namespace Tnzi.Notification.Services;

/// <summary>
/// 基于 MailKit 的邮件发送服务
/// </summary>
public class MailKitEmailSender : IEmailSender
{
    private readonly NotificationOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(
        NotificationOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<MailKitEmailSender> logger)
    {
        _options = Check.NotNull(options);
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _logger = Check.NotNull(logger);
    }

    public Task<SendResult> SendToAsync(string to, string? name, string subject, string body, bool isHtml = true, List<EmailAttachment>? attachments = null, CancellationToken cancellationToken = default)
    {
        // 单收件人只是多收件人的一个特例，走同一条发送路径，避免两条路径在开发重定向/附件处理上各自漂移
        return SendAsync(EmailMessage.Create(to, name, subject, body, isHtml, attachments), cancellationToken);
    }

    /// <inheritdoc />
    public Task<SendResult> SendToWithHeadersAsync(
        string to, string? name, string subject, string body, bool isHtml,
        List<EmailAttachment>? attachments,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken = default)
    {
        var message = EmailMessage.Create(to, name, subject, body, isHtml, attachments);
        if (headers is { Count: > 0 })
            message.Headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);

        return SendAsync(message, cancellationToken);
    }

    public async Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Check.NotNull(message);

        if (_options.MailSender == null)
        {
            _logger.LogWarning("Mail sender options not configured");
            return SendResult.CreateFailure("Mail sender options not configured");
        }

        var envelope = EmailEnvelope.Normalize(message);
        if (EmailEnvelope.HasNoRecipient(envelope))
        {
            _logger.LogWarning("Email has no recipient. Subject={Subject}", envelope.Subject);
            return SendResult.CreateFailure("Email has no recipient: To, Cc and Bcc are all empty");
        }

        // In development, redirect all outbound email to the configured override address
        var devOverride = _options.MailSender.DevOverrideEmail;
        if (!string.IsNullOrWhiteSpace(devOverride))
        {
            _logger.LogWarning("[DEV] Email redirected. OriginalRecipients={OriginalRecipients}, Override={Override}, Subject={Subject}", EmailEnvelope.Describe(envelope), devOverride, envelope.Subject);
            envelope = EmailEnvelope.RedirectTo(envelope, devOverride);
        }

        var recipients = EmailEnvelope.Describe(envelope);

        try
        {
            var mimeMessage = new MimeMessage();
            mimeMessage.From.Add(new MailboxAddress(_options.MailSender.FromName, _options.MailSender.FromEmail));
            AddAddresses(mimeMessage.To, envelope.To);
            AddAddresses(mimeMessage.Cc, envelope.Cc);
            AddAddresses(mimeMessage.Bcc, envelope.Bcc);
            mimeMessage.Subject = envelope.Subject;
            AddHeaders(mimeMessage, envelope.Headers);

            var bodyBuilder = new BodyBuilder();
            if (envelope.IsHtml)
            {
                bodyBuilder.HtmlBody = envelope.Body;
            }
            else
            {
                bodyBuilder.TextBody = envelope.Body;
            }

            // ★ 附件没能全部装上就不发。见 AddAttachmentsAsync 的说明：
            // 判定必须发生在连 SMTP **之前**，否则就是「发出一封残缺的信，然后报失败」——
            // 而失败会被续发路径捞回来重发，收件人于是收到两封都不对的信。
            var attachmentFailure = await AddAttachmentsAsync(bodyBuilder, envelope.Attachments, cancellationToken);
            if (attachmentFailure != null)
            {
                _logger.LogError("Not sending to {To}: {Reason}", recipients, attachmentFailure);
                return SendResult.CreateFailure(attachmentFailure);
            }

            mimeMessage.Body = bodyBuilder.ToMessageBody();

            using var client = new MailKit.Net.Smtp.SmtpClient();
            await client.ConnectAsync(_options.MailSender.SmtpServer, _options.MailSender.SmtpPort, _options.MailSender.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
            await client.AuthenticateAsync(_options.MailSender.Username, _options.MailSender.Password, cancellationToken);

            await client.SendAsync(mimeMessage, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            // ★ 返回这封信**真正的** Message-ID（信头里的那一个），不是另造一个追踪号。
            // MimeKit 在构造 MimeMessage 时就生成了它，随信发出去，于是它是唯一一个能把
            // 外部世界的后续消息对回这封信的东西：退信、邮件服务商日志、
            // 以及 email-to-fax 网关几分钟后回的那封确认邮件（它的 In-Reply-To / References
            // 指向的正是这个值）。自造的 `email-{时间戳}-{guid}` 从未出现在那封信里，
            // 拿它去对号永远对不上 —— 看着像个追踪 ID，实际什么也追踪不到。
            var messageId = mimeMessage.MessageId;

            _logger.LogInformation("Email sent successfully to {To}, MessageId: {MessageId}", recipients, messageId);
            return SendResult.CreateSuccess(messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", recipients);
            return SendResult.CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// 写入调用方给的额外信头（<c>List-Unsubscribe</c> 这类）。
    /// </summary>
    /// <remarks>
    /// ★ 空名字与含冒号或换行的名字一律跳过：MimeKit 会为它们抛异常，而**一个信头写不进去不该
    /// 让整封信发不出去**。信头是附加信息，正文才是这封信本身。
    /// </remarks>
    /// <remarks><c>internal</c> 是为了让测试直接检查写出来的信头 —— 经 mock 的
    /// <see cref="IEmailSender"/> 是看不见这一步的，而看不见就等于没有覆盖。</remarks>
    internal void AddHeaders(MimeMessage message, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is not { Count: > 0 })
            return;

        foreach (var (name, value) in headers)
        {
            if (string.IsNullOrWhiteSpace(name) || name.AsSpan().IndexOfAny(':', '\r', '\n') >= 0)
            {
                _logger.LogWarning("Skipping malformed email header name {HeaderName}", name);
                continue;
            }

            try
            {
                message.Headers.Add(name, value ?? string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Email header {HeaderName} could not be written; sending without it", name);
            }
        }
    }

    private static void AddAddresses(InternetAddressList target, List<EmailAddress> addresses)
    {
        foreach (var address in addresses)
        {
            target.Add(new MailboxAddress(address.Name ?? address.Address, address.Address));
        }
    }

    /// <summary>
    /// 把附件装进信体。返回 <see langword="null"/> 表示全部装上了，否则是这次投递的失败原因。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★ <b>取不到的附件是投递失败，不是可以记一条 warning 就往下走的降级。</b>
    /// 这里曾经三条失败分支都只写日志然后继续，于是信照发、<c>SendResult</c> 照样是成功、
    /// 收件人被记成 <c>Sent</c> —— 而实际到手的是一封没有发票的通知。
    /// </para>
    /// <para>
    /// ★ <b>传真上这件事是彻底的</b>：<c>FaxEnvelope</c> 按网关要求把正文置空，
    /// 一份传真的全部内容就是那个 PDF。附件丢了就是<b>发出一份什么都没有的传真并记为已投递</b>。
    /// 模块自己的注释早写着这条原则（「承载渠道发不出去，就别装作发得出去」），
    /// 只是那两道守卫都建在配置层面，覆盖不到发信那一刻的取件失败。
    /// </para>
    /// <para>
    /// ★ <b>为什么收集原因而不是抛异常</b>：调用方要在<b>连 SMTP 之前</b>就掉头。抛出去会被
    /// 外层 catch 记成 "Failed to send email"，读日志的人会以为是 SMTP 出了问题。
    /// </para>
    /// </remarks>
    private async Task<string?> AddAttachmentsAsync(BodyBuilder bodyBuilder, List<EmailAttachment>? attachments, CancellationToken cancellationToken)
    {
        if (attachments is not { Count: > 0 })
        {
            return null;
        }

        foreach (var attachment in attachments)
        {
            var contentType = ContentType.Parse(attachment.ContentType);

            // 优先使用内存数据
            if (attachment.Content != null)
            {
                bodyBuilder.Attachments.Add(attachment.FileName, attachment.Content, contentType);
            }
            else if (!string.IsNullOrEmpty(attachment.FilePath))
            {
                // 先判断 URL，再判断本地文件
                if (Uri.TryCreate(attachment.FilePath, UriKind.Absolute, out var uri) && !uri.IsFile)
                {
                    using var httpClient = _httpClientFactory.CreateClient();
                    try
                    {
                        using var stream = await httpClient.GetStreamAsync(uri, cancellationToken);
                        bodyBuilder.Attachments.Add(attachment.FileName, stream, contentType);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Failed to download attachment from URL: {FilePath}", attachment.FilePath);
                        return $"Attachment '{attachment.FileName}' could not be downloaded: {ex.Message}";
                    }
                }
                else if (File.Exists(attachment.FilePath))
                {
                    // ★ 带上已解析的 ContentType：不给的话 MimeKit 按扩展名嗅探，
                    // 而 FaxEnvelope 特意把 MIME 归一成 application/pdf 正是因为网关只认它。
                    await bodyBuilder.Attachments.AddAsync(attachment.FilePath, contentType, cancellationToken);
                }
                else
                {
                    _logger.LogWarning("Attachment file not found: {FileName}, FilePath: {FilePath}", attachment.FileName, attachment.FilePath);
                    return $"Attachment '{attachment.FileName}' was not found at the recorded path.";
                }
            }
            else
            {
                _logger.LogWarning("Attachment has no content and no file path: {FileName}", attachment.FileName);
                return $"Attachment '{attachment.FileName}' carries neither content nor a file path.";
            }
        }

        return null;
    }

}

