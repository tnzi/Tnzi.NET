using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace Tnzi.Notification.Push.Services;

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
            throw new ConfigurationException("Notification:PushSender", "Push sender options not configured.");

        if (string.IsNullOrWhiteSpace(_options.PushSender.FirebaseProjectId))
            throw new ConfigurationException("Notification:PushSender:FirebaseProjectId", "Firebase Project ID is not configured.");

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
                        // 走 CredentialFactory 而不是已弃用的 GoogleCredential.FromJson/FromFile：
                        // 后者按 JSON 内容动态挑凭据类型，Google 因安全风险弃用了它。这里的配置项
                        // 语义就是「服务账号 JSON」，显式指定 ServiceAccountCredential 也让配置放错时
                        // 在启动阶段报清楚，而不是拿一个错误类型的凭据去调 FCM 才失败。
                        if (!string.IsNullOrWhiteSpace(_options.PushSender.FirebaseServiceAccountJson))
                        {
                            // 从JSON字符串初始化
                            FirebaseApp.Create(new AppOptions
                            {
                                Credential = CredentialFactory
                                    .FromJson<ServiceAccountCredential>(_options.PushSender.FirebaseServiceAccountJson)
                                    .ToGoogleCredential(),
                                ProjectId = projectId
                            });
                        }
                        else if (!string.IsNullOrWhiteSpace(_options.PushSender.FirebaseServiceAccountJsonPath))
                        {
                            // 从文件路径初始化
                            FirebaseApp.Create(new AppOptions
                            {
                                Credential = CredentialFactory
                                    .FromFile<ServiceAccountCredential>(_options.PushSender.FirebaseServiceAccountJsonPath)
                                    .ToGoogleCredential(),
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
                // FirebaseAdmin 3.6.0 起把 Message.Token 标记为过时并指向 Fid，但两者
                // 不是改名：Token 序列化成 JSON 的 "token"（FCM 注册令牌），Fid 序列化成
                // "fid"（Firebase 安装 ID），是两个不同的标识符和两个不同的线缆字段。
                // 本方法的契约是 SendToAsync(string deviceToken, ...)，消费方传进来的
                // 就是客户端 SDK 拿到的注册令牌，直接改成 Fid 会让所有消费方的推送
                // 静默不再送达。改用 FID 需要消费方改变它们采集与存储的是什么标识符，
                // 那是一次契约变更而不是一次版本升级，因此这里就地抑制过时警告。
#pragma warning disable CS0618 // Type or member is obsolete
                Token = deviceToken,
#pragma warning restore CS0618
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