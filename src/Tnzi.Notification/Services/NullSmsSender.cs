namespace Tnzi.Notification.Services;

/// <summary>
/// 默认空实现，方便测试
/// </summary>
public class NullSmsSender : ISmsSender
{
    private readonly ILogger<NullSmsSender> _logger;

    public NullSmsSender(ILogger<NullSmsSender> logger) => _logger = Check.NotNull(logger);

    public Task<SendResult> SendToAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("NullSmsSender: Sending SMS to {PhoneNumber}: {Message}", phoneNumber, message);
        return Task.FromResult(SendResult.CreateSuccess("null-sender-mock-id"));
    }

}


