using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace Tnzi.Notification.Services;

/// <summary>
/// 推送通知服务实现
/// </summary>
public class PushSender : IPushSender
{
    private readonly NotificationOptions _options;
    private readonly ILogger<PushSender> _logger;
    private static readonly object _firebaseInitLock = new object();
    private static volatile bool _firebaseInitialized = false;

    public PushSender(NotificationOptions options, ILogger<PushSender> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    public async Task<SendResult> SendToAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default)
    {
        if (_options.PushSender == null)
        {
            _logger.LogWarning("Push sender options not configured");
            return SendResult.CreateFailure("Push sender options not configured");
        }

        try
        {
            switch (_options.PushSender.Provider.ToLower())
            {
                case "fcm":
                case "firebase":
                    return await SendViaFcmAsync(deviceToken, title, body, cancellationToken);
                case "apns":
                    return await SendViaApnsAsync(deviceToken, title, body, cancellationToken);
                default:
                    _logger.LogWarning("Unknown Push provider: {Provider}", _options.PushSender.Provider);
                    return SendResult.CreateFailure($"Unknown Push provider: {_options.PushSender.Provider}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send push notification to {DeviceToken}", deviceToken);
            return SendResult.CreateFailure(ex.Message);
        }
    }

    private async Task<SendResult> SendViaFcmAsync(string deviceToken, string title, string body, CancellationToken cancellationToken)
    {
        if (_options.PushSender == null)
            throw new Tnzi.Exceptions.ConfigurationException("Notification:PushSender", "Push sender options not configured.");

        if (string.IsNullOrWhiteSpace(_options.PushSender.FirebaseProjectId))
            throw new Tnzi.Exceptions.ConfigurationException("Notification:PushSender:FirebaseProjectId", "Firebase Project ID is not configured.");

        var projectId = _options.PushSender.FirebaseProjectId!;

        try
        {
            // 线程安全地初始化Firebase Admin SDK（如果尚未初始化）
            // 使用双重检查锁定模式确保线程安全
            if (!_firebaseInitialized && FirebaseApp.DefaultInstance == null)
            {
                lock (_firebaseInitLock)
                {
                    // 双重检查，避免在锁内重复初始化
                    if (!_firebaseInitialized && FirebaseApp.DefaultInstance == null)
                    {
                        if (!string.IsNullOrWhiteSpace(_options.PushSender.FirebaseServiceAccountJson))
                        {
                            // 从JSON字符串初始化
                            FirebaseApp.Create(new AppOptions
                            {
                                Credential = GoogleCredential.FromJson(_options.PushSender.FirebaseServiceAccountJson),
                                ProjectId = projectId
                            });
                        }
                        else if (!string.IsNullOrWhiteSpace(_options.PushSender.FirebaseServiceAccountJsonPath))
                        {
                            // 从文件路径初始化
                            FirebaseApp.Create(new AppOptions
                            {
                                Credential = GoogleCredential.FromFile(_options.PushSender.FirebaseServiceAccountJsonPath),
                                ProjectId = projectId
                            });
                        }
                        else
                        {
                            // 尝试使用默认凭据（例如环境变量GOOGLE_APPLICATION_CREDENTIALS）
                            FirebaseApp.Create(new AppOptions
                            {
                                ProjectId = projectId
                            });
                        }
                        _firebaseInitialized = true;
                    }
                }
            }

            var message = new FirebaseAdmin.Messaging.Message
            {
                Token = deviceToken,
                Notification = new FirebaseAdmin.Messaging.Notification
                {
                    Title = title,
                    Body = body
                }
            };

            var response = await FirebaseMessaging.DefaultInstance.SendAsync(message, cancellationToken);

            _logger.LogInformation("Push notification sent via FCM to {DeviceToken}, Message ID: {MessageId}",
                deviceToken, response);

            if (!string.IsNullOrWhiteSpace(response))
            {
                return SendResult.CreateSuccess(response);
            }
            else
            {
                return SendResult.CreateFailure("FCM returned empty message ID");
            }
        }
        catch (Exception ex)
        {
            // 未知异常使用Error级别
            _logger.LogError(ex, "Failed to send push notification via FCM to {DeviceToken}", deviceToken);
            return SendResult.CreateFailure(ex.Message);
        }
    }

    private async Task<SendResult> SendViaApnsAsync(string deviceToken, string title, string body, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Apple Push Notification Service (APNs) provider is not yet implemented. Push to {DeviceToken} was not sent.", deviceToken);
        return SendResult.CreateFailure(
            "Apple Push Notification Service (APNs) provider is not yet implemented. " +
            "Please install the APNs SDK and complete the implementation, " +
            "or use a different push provider.");
    }
}