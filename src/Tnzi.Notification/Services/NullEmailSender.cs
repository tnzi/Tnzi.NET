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
        _logger.LogInformation("NullEmailSender: Sending Email to {To}: {Subject}", to, subject);
        return Task.FromResult(SendResult.CreateSuccess("null-sender-mock-id"));
    }

}


