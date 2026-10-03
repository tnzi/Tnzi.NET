using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace Tnzi.Notification.Push.Services;

/// <summary>
/// 推送通知服务实现
/// </summary>
public class PushSender : IPushSender
{
    private readonly PushSenderOptions _options;
    private readonly string? _providerKey;
    private readonly ILogger<PushSender> _logger;
    private static readonly object _firebaseInitLock = new object();

    private readonly IPushDeviceService? _deviceService;
    private readonly bool _senderIdMismatchIsConclusive;

    /// <summary>
    /// 初始化一个 <see cref="PushSender"/>。
    /// </summary>
    /// <param name="options">这一个发送器的推送配置（默认的 <c>Notification:PushSender</c> 或具名的 <c>PushSenders:{key}</c> 一节）。</param>
    /// <param name="logger">日志。</param>
    /// <param name="deviceService">
    /// 设备注册表，用于在网关判定令牌永久失效时退役它。<b>可空</b>：
    /// 只用主题广播、或自己管理令牌的应用不需要它，缺席时投递行为完全不变，
    /// 只是死令牌不会被自动清理。
    /// </param>
    /// <param name="providerKey">
    /// 服务商键；<see langword="null"/> = 默认发送器。★ 它决定的是用<b>哪一个</b> <c>FirebaseApp</c>：
    /// 默认发送器用 SDK 的默认实例（宿主已自行引导过的也认），具名发送器各用一个以键命名的实例 ——
    /// 两个 Firebase 项目不能共用一个 <c>FirebaseApp</c>，谁先引导谁的凭据就是全部人的凭据。
    /// </param>
    /// <param name="senderIdMismatchIsConclusive">
    /// FCM 回 <c>SenderIdMismatch</c> 时能否据此退役令牌。★ 只有部署里<b>只有一个</b> Firebase 项目时才成立：
    /// 注册表不记令牌属于哪个项目，多项目部署下用项目 B 的发送器发给用户的全部令牌，项目 A 的令牌
    /// 必然回 <c>SenderIdMismatch</c> —— 那只说明「这个发送器发不到它」，不说明「谁都发不到它」，
    /// 按它退役会让项目 A 的推送永久到不了那台设备而零症状。模块按已配置的不同项目数决定这个值。
    /// </param>
    public PushSender(PushSenderOptions options, ILogger<PushSender> logger, IPushDeviceService? deviceService = null, string? providerKey = null, bool senderIdMismatchIsConclusive = true)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _deviceService = deviceService;
        _providerKey = NotificationProviderKeys.Normalize(providerKey);
        _senderIdMismatchIsConclusive = senderIdMismatchIsConclusive;
    }

    /// <summary>FCM 回 <c>SenderIdMismatch</c> 时是否退役令牌（见构造参数说明）。</summary>
    internal bool SenderIdMismatchIsConclusive => _senderIdMismatchIsConclusive;

    public async Task<SendResult> SendToAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default)
    {
        // ★ 主题地址填错了位置，就地说清楚，不要交给 FCM 去回一句「不是合法的注册令牌」。
        // 这条守的是通知管线那个入口：RecipientChannelDispatcher 对 NotificationType.Push
        // 一律走本方法（Recipient.Address 就是设备令牌），而主题投递刻意**不在**那条管线上
        // —— 退订按地址、偏好与频次上限按人，主题三者都没有。于是「把主题名填进收件人地址
        // 走一次群发」是可预见的误用，而它的远端症状指向的是令牌不合法，不是方法调错了。
        // ★ 这两处是本文件里仅有的**不打掩码**的地方，因为 LooksLikeTopicAddress 已经
        //   确定它以 /topics/ 开头 —— 那不是令牌，而调用方要看到自己填错的那个值。
        if (FcmTopicName.LooksLikeTopicAddress(deviceToken))
        {
            _logger.LogWarning(
                "Push delivery refused: {DeviceToken} is a topic address, not a device token.", deviceToken);
            return SendResult.CreateFailure(
                $"'{deviceToken}' is a topic address, not an FCM device token. "
                + "Call IPushSender.SendToTopicAsync to broadcast to a topic's subscribers. "
                + "Topic delivery is not available through the notification pipeline: "
                + "opt-out is keyed by address and preferences by user, and a topic has neither.");
        }

        try
        {
            switch (_options.Provider.ToLower())
            {
                case "fcm":
                case "firebase":
                    return await SendViaFcmAsync(deviceToken, title, body, cancellationToken);
                case "apns":
                    return await SendViaApnsAsync(deviceToken, title, body, cancellationToken);
                default:
                    _logger.LogWarning("Unknown Push provider: {Provider}", _options.Provider);
                    return SendResult.CreateFailure($"Unknown Push provider: {_options.Provider}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send push notification to {DeviceToken}", PushTokenMask.Of(deviceToken));
            return SendResult.CreateFailure(ex.Message);
        }
    }

    private async Task<SendResult> SendViaFcmAsync(string deviceToken, string title, string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.FirebaseProjectId))
            throw new ConfigurationException("Notification:PushSender:FirebaseProjectId", "Firebase Project ID is not configured.");

        var projectId = _options.FirebaseProjectId!;

        try
        {
            var app = EnsureFirebaseInitialized(projectId);

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

            var response = await FirebaseMessaging.GetMessaging(app).SendAsync(message, cancellationToken);

            _logger.LogInformation("Push notification sent via FCM to {DeviceToken}, Message ID: {MessageId}",
                PushTokenMask.Of(deviceToken), response);

            if (!string.IsNullOrWhiteSpace(response))
            {
                return SendResult.CreateSuccess(response);
            }
            else
            {
                return SendResult.CreateFailure("FCM returned empty message ID");
            }
        }
        catch (FirebaseMessagingException ex) when (IsTokenPermanentlyDead(ex.MessagingErrorCode, _senderIdMismatchIsConclusive))
        {
            // ★ 这是这张表唯一能得知令牌已死的时机。FCM 不会主动通知，卸载了 App 的
            // 客户端也不会回来注销 —— 不在这里退役，注册表就只增不减，而 admin 的
            // 「重试失败项」会对着一个永远不可能成功的令牌一直重试。
            _logger.LogWarning(ex,
                "FCM rejected the token as permanently invalid ({ErrorCode}); retiring the device. Token: {DeviceToken}",
                ex.MessagingErrorCode, PushTokenMask.Of(deviceToken));

            await RetireDeadTokenAsync(deviceToken);
            return SendResult.CreateFailure(ex.Message);
        }
        catch (Exception ex)
        {
            // 未知异常使用Error级别
            _logger.LogError(ex, "Failed to send push notification via FCM to {DeviceToken}", PushTokenMask.Of(deviceToken));
            return SendResult.CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// 这个错误码是否意味着<b>这个令牌</b>永远不会再投递成功。
    /// </summary>
    /// <remarks>
    /// ★ <b>只认这两个，刻意不含 <c>InvalidArgument</c>。</b>
    /// <list type="bullet">
    /// <item><c>Unregistered</c> —— 应用已卸载，或令牌已被轮换掉。</item>
    /// <item><c>SenderIdMismatch</c> —— 令牌属于另一个 Firebase 项目，用本部署的凭据
    ///   永远发不到它。★ <b>仅当部署只有一个 Firebase 项目时</b>
    ///   （<paramref name="senderIdMismatchIsConclusive"/>）：多项目部署下它只说明令牌属于另一个
    ///   已配置的项目，而那个项目的推送还要靠这一行。</item>
    /// </list>
    /// <c>InvalidArgument</c> 看着也像「令牌不对」，但 FCM 同样用它表示<b>消息本身</b>
    /// 不合法（字段越界、载荷过大之类）。按它退役等于:一次消息构造错误会把
    /// <b>这一批全部收件人的设备</b>从注册表里删光，而日志上只是一串投递失败。
    /// 少删是可恢复的（下次投递还会再判一次），多删不是。
    /// </remarks>
    internal static bool IsTokenPermanentlyDead(MessagingErrorCode? errorCode, bool senderIdMismatchIsConclusive)
        => errorCode == MessagingErrorCode.Unregistered
           || (senderIdMismatchIsConclusive && errorCode == MessagingErrorCode.SenderIdMismatch);

    /// <summary>
    /// 退役一个已死的令牌。<b>失败不改变这次投递的结论</b>。
    /// </summary>
    /// <remarks>
    /// 清理是收尾动作：它抛出来的异常会盖掉真正的失败原因（FCM 那一条），
    /// 让调用方看到一个与推送无关的数据库错误。所以这里就地吞掉并记日志 ——
    /// 这是「有真实补偿逻辑」的少数场景之一：补偿就是「这次没清掉，下次投递还会再判一次」。
    /// </remarks>
    private async Task RetireDeadTokenAsync(string deviceToken)
    {
        if (_deviceService == null)
            return;

        try
        {
            // ★ 刻意不传投递用的那个取消标记。RecipientChannelDispatcher 给每次投递套了
            // Notification:SendTimeoutSeconds 的超时，那个期限约束的是**网关调用**；
            // 而这里是一次本地清理。FCM 慢到快用完预算才回 Unregistered 时，
            // 沿用同一个标记会让清理被取消 —— 而它恰好在「网关正不正常」这件事上
            // 与失败相关，也就是说最需要退役的那些场景最容易退不掉，且只留一行日志。
            await _deviceService.RetireAsync(deviceToken, CancellationToken.None);
        }
        catch (Exception cleanupEx)
        {
            _logger.LogError(cleanupEx,
                "Could not retire the dead push token; it will be retried and re-detected on the next delivery.");
        }
    }

    private async Task<SendResult> SendViaApnsAsync(string deviceToken, string title, string body, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Apple Push Notification Service (APNs) provider is not yet implemented. Push to {DeviceToken} was not sent.", PushTokenMask.Of(deviceToken));
        return SendResult.CreateFailure(
            "Apple Push Notification Service (APNs) provider is not yet implemented. " +
            "Please install the APNs SDK and complete the implementation, " +
            "or use a different push provider.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// 结构与 <see cref="SendToAsync"/> 平行（先看配置、再按 provider 分派），只多一步：
    /// <b>主题名先过字符集校验</b>。校验放在分派之前是刻意的 —— 主题名是<b>调用方</b>给的，
    /// 而 provider 是<b>部署方</b>配的，先报调用方能自己改掉的那一个。
    /// </remarks>
    public async Task<SendResult> SendToTopicAsync(string topic, string title, string body, CancellationToken cancellationToken = default)
    {
        if (!FcmTopicName.TryValidate(topic, out var topicFailure))
        {
            _logger.LogWarning("Push topic delivery refused: {Reason}", topicFailure);
            return SendResult.CreateFailure(topicFailure!);
        }

        try
        {
            switch (_options.Provider.ToLower())
            {
                case "fcm":
                case "firebase":
                    return await SendViaFcmTopicAsync(topic, title, body, cancellationToken);
                case "apns":
                    // ★ 刻意不走 SendViaApnsAsync 那个存根。它说的是「装上 APNs SDK 再来」，
                    // 而装上了也不会有主题广播：APNs 根本没有 FCM 这种客户端自助订阅的主题
                    // （它的 apns-topic 是应用的 bundle id，是另一回事）。把人引向一条走不通的路，
                    // 比直接说不支持更费时间。iOS 要收主题广播，正确做法就是配 fcm ——
                    // FCM 自己会转投 APNs，消费方不需要也不应该为此直连 APNs。
                    _logger.LogWarning(
                        "Topic push delivery is not available for the apns provider. Topic {Topic} was not sent.", topic);
                    return SendResult.CreateFailure(
                        "Topic push delivery requires the fcm provider. "
                        + "The apns provider addresses individual devices only; APNs has no client-subscribed topics. "
                        + "Set Notification:PushSender:Provider to 'fcm' - FCM forwards to APNs for iOS devices.");
                default:
                    _logger.LogWarning("Unknown Push provider: {Provider}", _options.Provider);
                    return SendResult.CreateFailure($"Unknown Push provider: {_options.Provider}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send push notification to topic {Topic}", topic);
            return SendResult.CreateFailure(ex.Message);
        }
    }

    private async Task<SendResult> SendViaFcmTopicAsync(string topic, string title, string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.FirebaseProjectId))
            throw new ConfigurationException("Notification:PushSender:FirebaseProjectId", "Firebase Project ID is not configured.");

        var projectId = _options.FirebaseProjectId!;

        try
        {
            var app = EnsureFirebaseInitialized(projectId);

            var message = new FirebaseAdmin.Messaging.Message
            {
                // Topic 与 Token 是 Message 上互斥的寻址字段（FCM 只接受其中一个）。
                // 这里不设 Token —— 主题投递的全部意义就是后端手上一个设备标识符都没有。
                // /topics/ 前缀由 SDK 自己剥掉（Message.FormattedTopic），这里原样传。
                Topic = topic,
                Notification = new FirebaseAdmin.Messaging.Notification
                {
                    Title = title,
                    Body = body
                }
            };

            var response = await FirebaseMessaging.GetMessaging(app).SendAsync(message, cancellationToken);

            _logger.LogInformation("Push notification sent via FCM to topic {Topic}, Message ID: {MessageId}",
                topic, response);

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
            _logger.LogError(ex, "Failed to send push notification via FCM to topic {Topic}", topic);
            return SendResult.CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// 取这一个发送器要用的 <see cref="FirebaseApp"/>，进程内每个服务商键只引导一次。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b><see cref="FirebaseApp"/> 是进程级的、按名字登记的单例。</b>同名只能 <c>Create</c> 一次，
    /// 之后拿到的永远是第一份凭据。按令牌发和按主题发若各引导一次，用的是哪份凭据就取决于
    /// 哪条路径先被调到 —— 那是一个随请求时序变化、且没有任何日志的差异。两条路径因此共用这一处。
    /// </para>
    /// <para>
    /// ★ <b>具名发送器各用一个以键命名的实例</b>（<c>tnzi:{key}</c>），默认发送器用 SDK 的默认实例。
    /// 两个 Firebase 项目共用一个 <c>FirebaseApp</c> 是做不到的：第二份配置会被安静地忽略，
    /// 推送从另一个项目发出，客户端收不到而服务端记的是成功。默认发送器沿用默认实例，是为了
    /// 宿主或消费方已自行引导过 <c>FirebaseApp.DefaultInstance</c> 的场景不被重复引导。
    /// </para>
    /// <para>
    /// 双重检查锁定：先无锁查一次登记表，没有再进锁创建。<see cref="FirebaseApp.GetInstance(string)"/>
    /// 查不到返回 <see langword="null"/> 而不是抛，所以查询本身是廉价的。
    /// </para>
    /// </remarks>
    private FirebaseApp EnsureFirebaseInitialized(string projectId)
    {
        var existing = FindFirebaseApp();
        if (existing != null)
            return existing;

        lock (_firebaseInitLock)
        {
            // 双重检查，避免在锁内重复初始化
            existing = FindFirebaseApp();
            if (existing != null)
                return existing;

            // 走 CredentialFactory 而不是已弃用的 GoogleCredential.FromJson/FromFile：
            // 后者按 JSON 内容动态挑凭据类型，Google 因安全风险弃用了它。这里的配置项
            // 语义就是「服务账号 JSON」，显式指定 ServiceAccountCredential 也让配置放错时
            // 在启动阶段报清楚，而不是拿一个错误类型的凭据去调 FCM 才失败。
            var appOptions = new AppOptions { ProjectId = projectId };

            if (!string.IsNullOrWhiteSpace(_options.FirebaseServiceAccountJson))
            {
                // 从JSON字符串初始化
                appOptions.Credential = CredentialFactory
                    .FromJson<ServiceAccountCredential>(_options.FirebaseServiceAccountJson)
                    .ToGoogleCredential();
            }
            else if (!string.IsNullOrWhiteSpace(_options.FirebaseServiceAccountJsonPath))
            {
                // 从文件路径初始化
                appOptions.Credential = CredentialFactory
                    .FromFile<ServiceAccountCredential>(_options.FirebaseServiceAccountJsonPath)
                    .ToGoogleCredential();
            }
            // 否则交给默认凭据（例如环境变量 GOOGLE_APPLICATION_CREDENTIALS）

            return _providerKey == null
                ? FirebaseApp.Create(appOptions)
                : FirebaseApp.Create(appOptions, NamedAppName(_providerKey));
        }
    }

    /// <summary>这一个发送器的 <see cref="FirebaseApp"/> 若已引导则返回它，否则 <see langword="null"/>。</summary>
    private FirebaseApp? FindFirebaseApp()
        => _providerKey == null ? FirebaseApp.DefaultInstance : FirebaseApp.GetInstance(NamedAppName(_providerKey));

    /// <summary>
    /// 具名发送器的 <see cref="FirebaseApp"/> 名字。带前缀是为了不与宿主自己按别的名字引导的实例撞名。
    /// </summary>
    internal static string NamedAppName(string providerKey) => $"tnzi:{providerKey}";
}