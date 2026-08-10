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

    /// <summary>
    /// 发送一封邮件给多个收件人（To/Cc/Bcc 同在一封信里，To 与 Cc 中的收件人彼此可见）
    /// </summary>
    /// <remarks>
    /// 默认实现直接返回失败，既不退化成「逐个地址各发一封」，也不退化成「只发给第一个地址」：
    /// 这两种退化都会把一封抄送多方的函件悄悄换成另一种消息（收件人看不到还写给了谁、回复无法归到同一线程），
    /// 而且毫无症状。不支持多收件人的实现应当明确报错，由调用方决定怎么办。
    /// </remarks>
    Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(SendResult.CreateFailure(
            $"{GetType().Name} does not support multi-recipient email. Implement IEmailSender.SendAsync(EmailMessage) to deliver To/Cc/Bcc in a single message."));
    }
}

