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

            var bodyBuilder = new BodyBuilder();
            if (envelope.IsHtml)
            {
                bodyBuilder.HtmlBody = envelope.Body;
            }
            else
            {
                bodyBuilder.TextBody = envelope.Body;
            }

            await AddAttachmentsAsync(bodyBuilder, envelope.Attachments, cancellationToken);

            mimeMessage.Body = bodyBuilder.ToMessageBody();

            using var client = new MailKit.Net.Smtp.SmtpClient();
            await client.ConnectAsync(_options.MailSender.SmtpServer, _options.MailSender.SmtpPort, _options.MailSender.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
            await client.AuthenticateAsync(_options.MailSender.Username, _options.MailSender.Password, cancellationToken);

            await client.SendAsync(mimeMessage, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            // MailKit 不直接返回消息ID，使用时间戳和Guid生成唯一追踪ID
            // 格式：email-{timestamp}-{guid}，确保唯一性
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var uniqueId = Guid.NewGuid().ToString("N")[..8]; // 使用Guid的前8位确保唯一性
            var messageId = $"email-{timestamp}-{uniqueId}";

            _logger.LogInformation("Email sent successfully to {To}, MessageId: {MessageId}", recipients, messageId);
            return SendResult.CreateSuccess(messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", recipients);
            return SendResult.CreateFailure(ex.Message);
        }
    }

    private static void AddAddresses(InternetAddressList target, List<EmailAddress> addresses)
    {
        foreach (var address in addresses)
        {
            target.Add(new MailboxAddress(address.Name ?? address.Address, address.Address));
        }
    }

    private async Task AddAttachmentsAsync(BodyBuilder bodyBuilder, List<EmailAttachment>? attachments, CancellationToken cancellationToken)
    {
        if (attachments is not { Count: > 0 })
        {
            return;
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
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to download attachment from URL: {FilePath}", attachment.FilePath);
                    }
                }
                else if (File.Exists(attachment.FilePath))
                {
                    await bodyBuilder.Attachments.AddAsync(attachment.FilePath, cancellationToken);
                }
                else
                {
                    _logger.LogWarning("Attachment file not found: {FileName}, FilePath: {FilePath}", attachment.FileName, attachment.FilePath);
                }
            }
            else
            {
                _logger.LogWarning("Attachment has no content and no file path: {FileName}", attachment.FileName);
            }
        }
    }

}

