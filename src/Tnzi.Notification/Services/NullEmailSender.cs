namespace Tnzi.Notification.Services;

/// <summary>
/// 默认空实现，方便测试
/// </summary>
public class NullEmailSender : IEmailSender
{
    private readonly ILogger<NullEmailSender> _logger;

    public NullEmailSender(ILogger<NullEmailSender> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public Task<SendResult> SendToAsync(string to, string? name, string subject, string body, bool isHtml = true, List<EmailAttachment>? attachments = null, CancellationToken cancellationToken = default)
    {
        return SendAsync(EmailMessage.Create(to, name, subject, body, isHtml, attachments), cancellationToken);
    }

    public Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Check.NotNull(message);

        _logger.LogInformation("NullEmailSender: Sending Email to {To}: {Subject}", EmailEnvelope.Describe(message), message.Subject);
        return Task.FromResult(SendResult.CreateSuccess("null-sender-mock-id"));
    }

}

