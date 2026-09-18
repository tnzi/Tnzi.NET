namespace Tnzi.Notification.Services;

/// <summary>
/// 「配了具名短信发送器、却没配默认的那一个」时的默认 <see cref="ISmsSender"/>：每次调用都失败。
/// 理由与 <see cref="UnconfiguredEmailSender"/> 相同。
/// </summary>
public class UnconfiguredSmsSender : ISmsSender
{
    internal const string Guidance =
        "Named SMS senders are configured (Notification:SmsSenders) but no default SMS sender is. "
        + "Messages without a ProviderKey have nowhere to go: configure Notification:SmsSender, "
        + "register a default ISmsSender, or set ProviderKey on every SMS notification.";

    private readonly ILogger<UnconfiguredSmsSender> _logger;

    public UnconfiguredSmsSender(ILogger<UnconfiguredSmsSender> logger) => _logger = Check.NotNull(logger);

    public Task<SendResult> SendToAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        _logger.LogError("SMS to {PhoneNumber} refused: {Guidance}", phoneNumber, Guidance);
        return Task.FromResult(SendResult.CreateFailure(Guidance));
    }
}
