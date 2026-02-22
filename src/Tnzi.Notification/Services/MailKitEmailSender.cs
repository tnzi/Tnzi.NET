using MailKit.Net.Smtp;
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

    public async Task<SendResult> SendToAsync(string to, string? name, string subject, string body, bool isHtml = true, List<EmailAttachment>? attachments = null, CancellationToken cancellationToken = default)
    {
        if (_options.MailSender == null)
        {
            _logger.LogWarning("Mail sender options not configured");
            return SendResult.CreateFailure("Mail sender options not configured");
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.MailSender.FromName, _options.MailSender.FromEmail));
            message.To.Add(new MailboxAddress(name ?? to, to));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder();
            if (isHtml)
            {
                bodyBuilder.HtmlBody = body;
            }
            else
            {
                bodyBuilder.TextBody = body;
            }

            // 添加附件
            if (attachments != null && attachments.Any())
            {
                foreach (var attachment in attachments)
                {
                    if (File.Exists(attachment.FilePath))
                    {
                        await bodyBuilder.Attachments.AddAsync(attachment.FilePath, cancellationToken);
                    }
                    else if (Uri.TryCreate(attachment.FilePath, UriKind.Absolute, out var uri))
                    {
                        // 从URL下载附件
                        // 注意：IHttpClientFactory管理的HttpClient不需要手动Dispose，但使用using是良好的实践
                        using var httpClient = _httpClientFactory.CreateClient();
                        try
                        {
                            using var stream = await httpClient.GetStreamAsync(uri, cancellationToken);
                            bodyBuilder.Attachments.Add(attachment.FileName, stream, ContentType.Parse(attachment.ContentType));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to download attachment from URL: {FilePath}", attachment.FilePath);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Attachment file not found: {FilePath}", attachment.FilePath);
                    }
                }
            }

            message.Body = bodyBuilder.ToMessageBody();

            using var client = new MailKit.Net.Smtp.SmtpClient();
            await client.ConnectAsync(_options.MailSender.SmtpServer, _options.MailSender.SmtpPort, _options.MailSender.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
            await client.AuthenticateAsync(_options.MailSender.Username, _options.MailSender.Password, cancellationToken);
            
            var response = await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            // MailKit 不直接返回消息ID，使用时间戳和Guid生成唯一追踪ID
            // 格式：email-{timestamp}-{guid}，确保唯一性
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var uniqueId = Guid.NewGuid().ToString("N")[..8]; // 使用Guid的前8位确保唯一性
            var messageId = $"email-{timestamp}-{uniqueId}";
            
            _logger.LogInformation("Email sent successfully to {To}, MessageId: {MessageId}", to, messageId);
            return SendResult.CreateSuccess(messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", to);
            return SendResult.CreateFailure(ex.Message);
        }
    }

}

