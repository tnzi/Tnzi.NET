namespace Tnzi.Notification.Services;

/// <summary>
/// 未配置传真渠道时的回退实现：每次调用都失败。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>刻意不叫 <c>NullFaxSender</c>，也刻意不照 <see cref="NullEmailSender"/> /
/// <see cref="NullSmsSender"/> / <see cref="NullPushSender"/> 那样返回成功。</b>
/// 那三个返回成功在它们的场景里说得过去（开发期不想真发短信）；传真不行 ——
/// 一份传真通常是**函件本身**（对方律师、保险公司、法院），"发过了"这件事会被拿去交差。
/// 未配置时报成功，等于给每一份没发出去的传真开一张已投递的证明，而这种谎**没有任何症状**：
/// 日志干净、状态是 Sent、没有退信可查，直到几周后对方说没收到。
/// </para>
/// <para>
/// 这与 <see cref="IEmailSender.SendAsync"/> 的默认实现是同一条取舍：宁可当场失败，
/// 也不要一条能工作的降级路径。名字直说条件（<c>Unconfigured</c>），
/// 免得有人按 <c>Null*</c> 的类比推断它的行为。
/// </para>
/// </remarks>
public class UnconfiguredFaxSender : IFaxSender
{
    private readonly ILogger<UnconfiguredFaxSender> _logger;

    /// <summary>初始化一个 <see cref="UnconfiguredFaxSender"/> 实例。</summary>
    public UnconfiguredFaxSender(ILogger<UnconfiguredFaxSender> logger) => _logger = Check.NotNull(logger);

    /// <inheritdoc />
    public Task<SendResult> SendToAsync(string faxNumber, EmailAttachment document, string? subject = null, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Fax to {FaxNumber} was not sent: no fax sender is configured (Notification:FaxSender)", faxNumber);

        return Task.FromResult(SendResult.CreateFailure(
            "No fax sender is configured. Set Notification:FaxSender:GatewayDomain to deliver through an email-to-fax gateway, or register your own IFaxSender."));
    }
}
