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
    /// 发送邮件到单个接收者，并带上额外的信头（如 RFC 8058 的 <c>List-Unsubscribe</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>默认实现丢掉信头、退回 <see cref="SendToAsync(string, string?, string, string, bool, List{EmailAttachment}?, CancellationToken)"/>。</b>
    /// 自定义发送器因此照常把信发出去，只是不带那个信头 —— <b>少一个信头是可以降级的，
    /// 发不出去不是。</b>要支持一键退订按钮，重写本方法即可。
    /// </para>
    /// <para>
    /// 刻意<b>另起一个名字</b>而不是给 <c>SendToAsync</c> 加一个可选参数：加参数会让现有实现的
    /// 签名对不上（接口成员不是"可选参数就兼容"的），而重载会在 6 参调用点上产生歧义。
    /// </para>
    /// </remarks>
    Task<SendResult> SendToWithHeadersAsync(
        string to, string? name, string subject, string body, bool isHtml,
        List<EmailAttachment>? attachments,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken = default)
        => SendToAsync(to, name, subject, body, isHtml, attachments, cancellationToken);

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

