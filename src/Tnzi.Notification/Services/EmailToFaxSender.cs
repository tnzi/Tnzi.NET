namespace Tnzi.Notification.Services;

/// <summary>
/// 默认的传真发送实现：经 email-to-fax 网关投递。
/// </summary>
/// <remarks>
/// <para>
/// 收件地址 = <c>{归一化后的号码}@{网关域名}</c>，正文为空，恰好一个 PDF 附件。
/// 架在 <see cref="IEmailSender"/> 之上，所以已经配好 SMTP 的部署不需要任何新设施 ——
/// 这正是商用传真今天的实际形态。
/// </para>
/// <para>
/// ★ <b>演示/预发的重定向不是另一条分支</b>：邮件照常按发往网关的样子组装完（同一份附件、
/// 同样的空正文、同样的主题），只在交给 <see cref="IEmailSender"/> 之前把收件人换掉，
/// 用的还是邮件渠道那个 <see cref="EmailEnvelope.RedirectTo"/>。这条很重要 ——
/// 消费应用会在重定向模式下跑上几个月，如果重定向是一条独立分支，那几个月里被反复验证的
/// 就是**另一条**代码路径，等到切生产的那天才第一次跑真正要用的那条。原本的收件地址
/// （里面带着传真号码）写进主题，所以在收件箱里看得出这份传真本来要发给谁。
/// </para>
/// </remarks>
public class EmailToFaxSender : IFaxSender
{
    private readonly FaxSenderOptions _options;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<EmailToFaxSender> _logger;

    /// <summary>初始化一个 <see cref="EmailToFaxSender"/> 实例。</summary>
    /// <param name="options">
    /// 这一个发送器的传真配置：默认发送器是 <c>Notification:FaxSender</c>，具名发送器是
    /// <c>Notification:FaxSenders:{key}</c> 里的一节。
    /// </param>
    /// <param name="emailSender">
    /// 承载它的邮件发送器 —— 由 <see cref="FaxSenderOptions.EmailProviderKey"/> 决定是默认的还是某个具名的，
    /// 模块注册时已经解析好，这里只管用。
    /// </param>
    /// <param name="logger">日志。</param>
    public EmailToFaxSender(FaxSenderOptions options, IEmailSender emailSender, ILogger<EmailToFaxSender> logger)
    {
        _options = Check.NotNull(options);
        _emailSender = Check.NotNull(emailSender);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<SendResult> SendToAsync(string faxNumber, EmailAttachment document, string? subject = null, CancellationToken cancellationToken = default)
    {
        var fax = _options;
        if (!fax.Enabled || string.IsNullOrWhiteSpace(fax.GatewayDomain))
        {
            _logger.LogWarning("Fax sender options not configured");
            return SendResult.CreateFailure("Fax sender options not configured: set Notification:FaxSender:GatewayDomain and leave Enabled at true.");
        }

        // ★ 承载渠道发不出去，就别装作发得出去：SMTP 未配置时 IEmailSender 是 NullEmailSender，
        // 而它**返回成功** —— 不拦这一道，每一份传真都会被记成已投递，一份都没发出去。
        // ★★ 判据是「手上这个 sender 会不会真的发信」，不是「MailSender 那一节配没配」：
        // 消费应用注册自己的 IEmailSender（SendGrid / Postmark 这类 API 发信）时根本不会配 SMTP 那一节，
        // 按配置判会把一个邮件通道完全正常的部署判成发不了传真，还把人引向"去配 SMTP"。
        if (_emailSender is NullEmailSender)
        {
            _logger.LogWarning("Fax is configured but no real mail sender is; email-to-fax has nothing to deliver through");
            return SendResult.CreateFailure(
                "Fax is delivered through email-to-fax, but no mail sender is configured (Notification:MailSender). " +
                "Configure SMTP, register your own IEmailSender, or register your own IFaxSender.");
        }

        if (!FaxEnvelope.TryBuild(faxNumber, fax.GatewayDomain, document, subject, out var message, out var error))
        {
            _logger.LogWarning("Fax to {FaxNumber} rejected before sending: {Reason}", faxNumber, error);
            return SendResult.CreateFailure(error!);
        }

        var gatewayAddress = EmailEnvelope.Describe(message!);

        var devOverride = fax.DevOverrideEmail;
        if (!string.IsNullOrWhiteSpace(devOverride))
        {
            _logger.LogWarning("[DEV] Fax redirected. IntendedGateway={IntendedGateway}, Override={Override}", gatewayAddress, devOverride);
            message = EmailEnvelope.RedirectTo(message!, devOverride);
        }

        var result = await _emailSender.SendAsync(message!, cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation("Fax handed to the gateway {Gateway}, MessageId: {MessageId}", gatewayAddress, result.ExternalMessageId);
        }
        else
        {
            _logger.LogError("Failed to hand fax to the gateway {Gateway}: {Reason}", gatewayAddress, result.FailureReason);
        }

        return result;
    }
}
