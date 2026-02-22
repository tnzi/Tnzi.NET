namespace Tnzi.Notification.Services;

/// <summary>
/// 默认空实现，方便测试
/// </summary>
public class NullPushSender : IPushSender
{
    private readonly ILogger<NullPushSender> _logger;

    public NullPushSender(ILogger<NullPushSender> logger) => _logger = Check.NotNull(logger);

    public Task<SendResult> SendToAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NullPushSender: Sending Push to {DeviceToken}: {Title}", deviceToken, title);
        return Task.FromResult(SendResult.CreateSuccess("null-sender-mock-id"));
    }

}


