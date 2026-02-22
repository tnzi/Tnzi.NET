namespace Tnzi.Notification.Services;

/// <summary>
/// 邮件发送服务接口
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// 发送邮件到单个接收者
    /// </summary>
    Task<SendResult> SendToAsync(string to, string? name, string subject, string body, bool isHtml = true, List<EmailAttachment>? attachments = null, CancellationToken cancellationToken = default);

}

