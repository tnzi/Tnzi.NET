namespace Tnzi.Notification.Options;

/// <summary>
/// 通知模块配置选项
/// 配置路径：Notification
/// </summary>
[ConfigSection("Notification")]
[RuntimeSettingGroup(Key = "notification-general", Module = "Notification", DisplayName = "General",
    Icon = "mdi:bell-cog-outline", Order = 400, I18nKey = "admin.modules.system.settings.groups.notificationGeneral")]
public class NotificationOptions
{
    /// <summary>
    /// 获取或设置 邮件发送配置
    /// </summary>
    public MailSenderOptions? MailSender { get; set; }

    /// <summary>
    /// 获取或设置 短信发送配置
    /// </summary>
    public SmsSenderOptions? SmsSender { get; set; }

    /// <summary>
    /// 获取或设置 Push推送配置
    /// </summary>
    public PushSenderOptions? PushSender { get; set; }

    /// <summary>
    /// 获取或设置 队列配置
    /// </summary>
    public QueueOptions Queue { get; set; } = new();

    /// <summary>
    /// 获取或设置 最大并发数（批量发送时）
    /// </summary>
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>
    /// 获取或设置 发送超时（秒）
    /// </summary>
    [RuntimeSetting(Label = "Send Timeout (s)", I18n = "admin.modules.system.settings.fields.sendTimeoutSeconds",
        Type = SettingFieldType.Int, Min = 1)]
    public int SendTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 获取或设置 SMS 最大内容长度。
    /// KEEP-STATIC：当前无运行时消费者（发送路径未接线截断/校验），暴露会造成"假热配"。
    /// </summary>
    public int SmsMaxContentLength { get; set; } = 1600;

    /// <summary>
    /// 获取或设置 重试配置
    /// </summary>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>
    /// 获取或设置 派发（恢复 + 限速）配置
    /// </summary>
    public DispatchOptions Dispatch { get; set; } = new();

    /// <summary>
    /// 获取或设置 退订配置
    /// </summary>
    public OptOutOptions OptOut { get; set; } = new();
}

/// <summary>
/// 退订配置。
/// </summary>
[ConfigSection("Notification:OptOut")]
public class OptOutOptions
{
    /// <summary>
    /// 一键退订令牌的签名密钥。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>未配置时签发令牌会直接抛异常，这是刻意的。</b>换成一个内置默认密钥会让签名形同虚设 ——
    /// 任何知道这个框架的人都能替任意地址退订，而这种失效不会有任何症状：链接照常工作，
    /// 直到有人发现自己"被退订"了。宁可在部署时炸，也不要发出一批可伪造的链接。
    /// </para>
    /// <para>密钥即配置，可跨环境迁移；与 <c>AesGcmHelper</c> 的取舍一致。</para>
    /// </remarks>
    public string? TokenSecret { get; set; }
}

/// <summary>
/// 派发配置：进程重启后的续发恢复，以及发送节奏。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要恢复。</b>收件人状态本来就逐行持久化（<c>Recipient.Status</c>），所以进程中途退出
/// 并不丢数据 —— 但也<b>没有任何东西会去把它接着发完</b>：消息停在 <c>Sending</c>，剩下的收件人
/// 停在 <c>Pending</c>，除非有人手工调 <c>RetryAsync</c>。对群发来说这等于"发了一半，没人知道"。
/// </para>
/// <para>
/// 恢复是<b>幂等</b>的：续发只挑 <c>Pending</c> / <c>Failed</c> 的收件人（已 <c>Sent</c> 的不会重发），
/// 这是既有发送路径本来的行为，恢复只是把它重新触发一次。
/// </para>
/// <para>
/// <b>为什么带 <c>[RuntimeSettingGroup]</c> 且并入 <c>notification-general</c>。</b>本类此前只有
/// <c>[ConfigSection]</c>：缺了组特性，<c>RuntimeSettingMetadataExtractor</c> 就拿配置节字符串顶替组元数据，
/// 派生出 <c>ModuleName = "Notification:Dispatch"</c>，进而是权限组 <c>notificationdispatch</c> ——
/// 一个谁都没声明的组，于是 <c>PermissionDbSeeder</c> 记一行 warning 就把这一组的 view/update 两个码丢了
/// （配置中心里这四个字段从此只有超管能改）。顺带还让 admin 侧栏出现一张标题写着 <c>Notification:Dispatch</c>、
/// 无图标、<c>Order = 0</c> 排在最前的卡片。并入而不是另开一组，是照同文件 <c>RetryOptions</c> 的既有取舍：
/// 派发节奏与重试节奏同属"发送行为"，运维在一张卡里调完。
/// </para>
/// </remarks>
[ConfigSection("Notification:Dispatch")]
[RuntimeSettingGroup(Key = "notification-general", Module = "Notification", DisplayName = "General",
    Icon = "mdi:bell-cog-outline", Order = 400, I18nKey = "admin.modules.system.settings.groups.notificationGeneral")]
public class DispatchOptions
{
    /// <summary>
    /// 获取或设置 是否启用重启续发恢复（默认 true）
    /// </summary>
    [RuntimeSetting(Label = "Enable Recovery", I18n = "admin.modules.system.settings.fields.notificationEnableRecovery",
        Type = SettingFieldType.Boolean)]
    public bool EnableRecovery { get; set; } = true;

    /// <summary>
    /// 获取或设置 恢复扫描间隔（分钟，默认 5）
    /// </summary>
    [RuntimeSetting(Label = "Recovery Interval (min)", I18n = "admin.modules.system.settings.fields.notificationRecoveryIntervalMinutes",
        Type = SettingFieldType.Int, Min = 1)]
    public int RecoveryIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// 获取或设置 判定"卡住"的时长（分钟，默认 15）。
    /// </summary>
    /// <remarks>
    /// 一条正在正常发送中的消息也处于 <c>Sending</c>，所以不能一看到 <c>Sending</c> 就抢。
    /// 只有<b>超过这个时长仍未推进</b>的才认定是被中断的批次。取值应明显大于一次正常群发的耗时。
    /// </remarks>
    [RuntimeSetting(Label = "Stuck After (min)", I18n = "admin.modules.system.settings.fields.notificationStuckAfterMinutes",
        Type = SettingFieldType.Int, Min = 1)]
    public int StuckAfterMinutes { get; set; } = 15;

    /// <summary>
    /// 获取或设置 每分钟发送上限（0 = 不限速）。
    /// </summary>
    /// <remarks>
    /// 群发不限速会触发服务商的滥用防护 —— 结果不是发得慢，是<b>整个账号被封</b>，
    /// 连正常的密码重置邮件一起停摆。设成服务商配额的一个保守分数。
    /// </remarks>
    [RuntimeSetting(Label = "Rate Per Minute", I18n = "admin.modules.system.settings.fields.notificationRatePerMinute",
        Type = SettingFieldType.Int, Min = 0)]
    public int RatePerMinute { get; set; }

    /// <summary>
    /// 获取或设置 单次恢复扫描最多接手的消息数（默认 50），防止一次扫描独占整个派发窗口。
    /// </summary>
    public int RecoveryBatchSize { get; set; } = 50;
}


/// <summary>
/// 队列配置选项
/// </summary>
public class QueueOptions
{
    /// <summary>
    /// 获取或设置 是否启用队列
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 获取或设置 队列容量（仅用于内存队列，默认10000）
    /// </summary>
    public int QueueCapacity { get; set; } = 10000;
}

/// <summary>
/// 重试配置选项
/// </summary>
[ConfigSection("Notification:Retry")]
[RuntimeSettingGroup(Key = "notification-general", Module = "Notification", DisplayName = "General",
    Icon = "mdi:bell-cog-outline", Order = 400, I18nKey = "admin.modules.system.settings.groups.notificationGeneral")]
public class RetryOptions
{
    /// <summary>
    /// 获取或设置 重试延迟（秒）
    /// </summary>
    [RuntimeSetting(Label = "Retry Delay (s)", I18n = "admin.modules.system.settings.fields.retryDelaySeconds",
        Type = SettingFieldType.Int, Min = 0)]
    public int RetryDelaySeconds { get; set; } = 60;

    /// <summary>
    /// 获取或设置 是否启用指数退避
    /// </summary>
    [RuntimeSetting(Label = "Exponential Backoff", I18n = "admin.modules.system.settings.fields.enableExponentialBackoff",
        Type = SettingFieldType.Boolean)]
    public bool EnableExponentialBackoff { get; set; } = true;
}

/// <summary>
/// 邮件发送配置选项
/// </summary>
public class MailSenderOptions
{
    /// <summary>
    /// 获取或设置 SMTP服务器地址
    /// </summary>
    public string SmtpServer { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 SMTP端口
    /// </summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>
    /// 获取或设置 用户名
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 密码
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 是否启用SSL
    /// </summary>
    public bool EnableSsl { get; set; } = true;

    /// <summary>
    /// 获取或设置 发件人邮箱
    /// </summary>
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 发件人名称
    /// </summary>
    public string FromName { get; set; } = string.Empty;

    /// <summary>
    /// Development override: when set, all outbound emails are redirected to this address.
    /// Configure via "Notification:MailSender:DevOverrideEmail" in appsettings.Development.json.
    /// </summary>
    public string? DevOverrideEmail { get; set; }
}

/// <summary>
/// 短信发送配置选项
/// </summary>
public class SmsSenderOptions
{
    /// <summary>
    /// 获取或设置 短信服务提供商 (twilio, plivo)
    /// </summary>
    public string Provider { get; set; } = "twilio";

    /// <summary>
    /// 获取或设置 Twilio Account SID (当Provider为twilio时使用)
    /// </summary>
    public string TwilioAccountSid { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 Twilio Auth Token (当Provider为twilio时使用)
    /// </summary>
    public string TwilioAuthToken { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 Twilio From Phone Number (当Provider为twilio时使用)
    /// </summary>
    public string TwilioFromPhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 Plivo Auth ID (当Provider为plivo时使用)
    /// </summary>
    public string PlivoAuthId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 Plivo Auth Token (当Provider为plivo时使用)
    /// </summary>
    public string PlivoAuthToken { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 Plivo From Phone Number (当Provider为plivo时使用)
    /// </summary>
    public string PlivoFromPhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Development override: when set, all outbound SMS are redirected to this phone number.
    /// Configure via "Notification:SmsSender:DevOverridePhone" in appsettings.Development.json.
    /// </summary>
    public string? DevOverridePhone { get; set; }
}

/// <summary>
/// Push推送配置选项
/// </summary>
public class PushSenderOptions
{
    /// <summary>
    /// 获取或设置 Push服务提供商 (fcm, firebase)
    /// </summary>
    public string Provider { get; set; } = "fcm";

    /// <summary>
    /// 获取或设置 Firebase项目ID (当Provider为fcm/firebase时使用)
    /// </summary>
    public string FirebaseProjectId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 Firebase服务账号JSON文件路径 (当Provider为fcm/firebase时使用)
    /// </summary>
    public string FirebaseServiceAccountJsonPath { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 Firebase服务账号JSON内容 (当Provider为fcm/firebase时使用，优先级高于文件路径)
    /// </summary>
    public string? FirebaseServiceAccountJson { get; set; }
}

/// <summary>
/// Notification配置验证器
/// </summary>
public class NotificationOptionsValidator : OptionsValidatorBase<NotificationOptions>
{
    protected override void ValidateOptions(NotificationOptions options, List<string> errors)
    {
        // 验证各个配置部分
        ValidateMailSenderOptions(options.MailSender, errors);
        ValidateSmsSenderOptions(options.SmsSender, errors);
        ValidatePushSenderOptions(options.PushSender, errors);
        ValidateQueueOptions(options.Queue, errors);
        ValidateCommonOptions(options, errors);
    }

    /// <summary>
    /// 验证邮件发送配置
    /// </summary>
    private static void ValidateMailSenderOptions(MailSenderOptions? mailSender, List<string> errors)
    {
        if (mailSender == null)
            return;

        if (string.IsNullOrWhiteSpace(mailSender.SmtpServer))
            errors.Add("MailSender.SmtpServer is required.");

        if (mailSender.SmtpPort <= 0 || mailSender.SmtpPort > 65535)
            errors.Add("MailSender.SmtpPort must be between 1 and 65535.");

        if (string.IsNullOrWhiteSpace(mailSender.FromEmail))
            errors.Add("MailSender.FromEmail is required.");

        if (!string.IsNullOrWhiteSpace(mailSender.FromEmail) &&
            !Regex.IsMatch(mailSender.FromEmail,
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase))
            errors.Add("MailSender.FromEmail must be a valid email address.");

        // 如果启用了 SSL，验证用户名和密码
        if (mailSender.EnableSsl)
        {
            if (string.IsNullOrWhiteSpace(mailSender.Username))
                errors.Add("MailSender.Username is required when EnableSsl is true.");

            if (string.IsNullOrWhiteSpace(mailSender.Password))
                errors.Add("MailSender.Password is required when EnableSsl is true.");
        }
    }

    /// <summary>
    /// 验证短信发送配置
    /// </summary>
    private static void ValidateSmsSenderOptions(SmsSenderOptions? smsSender, List<string> errors)
    {
        if (smsSender == null)
            return;

        var provider = smsSender.Provider?.ToLower() ?? string.Empty;

        if (provider == "twilio")
        {
            if (string.IsNullOrWhiteSpace(smsSender.TwilioAccountSid))
                errors.Add("SmsSender.TwilioAccountSid is required when Provider is 'twilio'.");

            if (string.IsNullOrWhiteSpace(smsSender.TwilioAuthToken))
                errors.Add("SmsSender.TwilioAuthToken is required when Provider is 'twilio'.");

            if (string.IsNullOrWhiteSpace(smsSender.TwilioFromPhoneNumber))
                errors.Add("SmsSender.TwilioFromPhoneNumber is required when Provider is 'twilio'.");
        }
        else if (provider == "plivo")
        {
            if (string.IsNullOrWhiteSpace(smsSender.PlivoAuthId))
                errors.Add("SmsSender.PlivoAuthId is required when Provider is 'plivo'.");

            if (string.IsNullOrWhiteSpace(smsSender.PlivoAuthToken))
                errors.Add("SmsSender.PlivoAuthToken is required when Provider is 'plivo'.");

            if (string.IsNullOrWhiteSpace(smsSender.PlivoFromPhoneNumber))
                errors.Add("SmsSender.PlivoFromPhoneNumber is required when Provider is 'plivo'.");
        }
        else if (!string.IsNullOrWhiteSpace(provider))
        {
            errors.Add($"SmsSender.Provider '{smsSender.Provider}' is not supported. Supported providers: twilio, plivo.");
        }
    }

    /// <summary>
    /// 验证推送通知配置
    /// </summary>
    private static void ValidatePushSenderOptions(PushSenderOptions? pushSender, List<string> errors)
    {
        if (pushSender == null)
            return;

        var provider = pushSender.Provider?.ToLower() ?? string.Empty;

        if (provider == "fcm" || provider == "firebase")
        {
            if (string.IsNullOrWhiteSpace(pushSender.FirebaseProjectId))
                errors.Add("PushSender.FirebaseProjectId is required when Provider is 'fcm' or 'firebase'.");

            if (string.IsNullOrWhiteSpace(pushSender.FirebaseServiceAccountJson) &&
                string.IsNullOrWhiteSpace(pushSender.FirebaseServiceAccountJsonPath))
            {
                errors.Add("PushSender.FirebaseServiceAccountJson or FirebaseServiceAccountJsonPath is required when Provider is 'fcm' or 'firebase'.");
            }
            else if (!string.IsNullOrWhiteSpace(pushSender.FirebaseServiceAccountJsonPath))
            {
                // 注意：不验证文件路径是否存在，因为文件可能在运行时才创建
                // 如果文件不存在，会在实际使用时失败并记录错误
            }
            else if (!string.IsNullOrWhiteSpace(pushSender.FirebaseServiceAccountJson))
            {
                // 验证 JSON 内容是否有效
                try
                {
                    JsonDocument.Parse(pushSender.FirebaseServiceAccountJson);
                }
                catch (JsonException)
                {
                    errors.Add("PushSender.FirebaseServiceAccountJson is not valid JSON.");
                }
            }
        }
        else if (provider == "apns")
        {
            errors.Add("PushSender.Provider 'apns' is not yet implemented.");
        }
        else if (!string.IsNullOrWhiteSpace(provider))
        {
            errors.Add($"PushSender.Provider '{pushSender.Provider}' is not supported. Supported providers: fcm, firebase.");
        }
    }

    /// <summary>
    /// 验证队列配置
    /// </summary>
    private static void ValidateQueueOptions(QueueOptions queue, List<string> errors)
    {
        if (!queue.Enabled)
            return;

        if (queue.QueueCapacity <= 0)
            errors.Add("Queue.QueueCapacity must be greater than 0.");
    }

    /// <summary>
    /// 验证通用配置选项
    /// </summary>
    private static void ValidateCommonOptions(NotificationOptions options, List<string> errors)
    {
        // 验证并发配置
        if (options.MaxConcurrency <= 0)
            errors.Add("MaxConcurrency must be greater than 0.");

        // 验证超时配置
        if (options.SendTimeoutSeconds <= 0)
            errors.Add("SendTimeoutSeconds must be greater than 0.");

        // 验证 SMS 最大长度配置
        if (options.SmsMaxContentLength <= 0)
            errors.Add("SmsMaxContentLength must be greater than 0.");

        // 验证重试配置
        if (options.Retry.RetryDelaySeconds < 0)
            errors.Add("Retry.RetryDelaySeconds must be greater than or equal to 0.");
    }
}

