namespace Tnzi.Notification.Services;

/// <summary>
/// 「配了具名邮件发送器、却没配默认的那一个」时的默认 <see cref="IEmailSender"/>：每次调用都失败。
/// </summary>
/// <remarks>
/// <para>
/// 没配任何邮件发送器的部署走 <see cref="NullEmailSender"/>（开发期不真发，报成功），那是「这个部署不发信」。
/// 但 <c>Notification:MailSenders</c> 里有东西的部署<b>显然要发信</b> —— 此时一条没带 <c>ProviderKey</c>
/// 的消息落到一个报成功的空实现上，是路由错误不是开发模式：验证码「发送成功」而没有人收到，且毫无症状。
/// </para>
/// <para>
/// 修法三选一，失败原因里都写着：补上 <c>Notification:MailSender</c>、在代码里注册默认的 <see cref="IEmailSender"/>、
/// 或者给每条消息都带上键。消费方注册自己的默认发送器时，本类根本不会被选中（普通注册赢过 <c>TryAdd</c>）。
/// </para>
/// </remarks>
public class UnconfiguredEmailSender : IEmailSender
{
    internal const string Guidance =
        "Named mail senders are configured (Notification:MailSenders) but no default mail sender is. "
        + "Messages without a ProviderKey have nowhere to go: configure Notification:MailSender, "
        + "register a default IEmailSender, or set ProviderKey on every email notification.";

    private readonly ILogger<UnconfiguredEmailSender> _logger;

    public UnconfiguredEmailSender(ILogger<UnconfiguredEmailSender> logger) => _logger = Check.NotNull(logger);

    public Task<SendResult> SendToAsync(string to, string? name, string subject, string body, bool isHtml = true, List<EmailAttachment>? attachments = null, CancellationToken cancellationToken = default)
    {
        _logger.LogError("Email to {To} refused: {Guidance}", to, Guidance);
        return Task.FromResult(SendResult.CreateFailure(Guidance));
    }

    public Task<SendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Check.NotNull(message);
        _logger.LogError("Email to {To} refused: {Guidance}", EmailEnvelope.Describe(message), Guidance);
        return Task.FromResult(SendResult.CreateFailure(Guidance));
    }
}
