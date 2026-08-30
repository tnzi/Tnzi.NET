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
    /// 获取或设置 传真发送配置（email-to-fax 网关）
    /// </summary>
    public FaxSenderOptions? FaxSender { get; set; }

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

    /// <summary>
    /// 退订落地页的<b>绝对</b> URL，例如 <c>https://app.example.com/unsubscribe</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 配了它，框架才会在群发邮件上写 RFC 8058 的 <c>List-Unsubscribe</c> /
    /// <c>List-Unsubscribe-Post</c> 信头 —— 也就是 Gmail / Outlook / Apple Mail 顶部那个
    /// 「退订」按钮。没有它就没有信头（不猜、不用相对地址）：一个指向错主机的退订链接
    /// 比没有退订更糟，因为它看着能用。
    /// </para>
    /// <para>
    /// ★ <b>为什么本模块自己存一份 URL，而不去读 <c>Application:FrontendUrl</c></b>：那个选项住在
    /// <c>Tnzi.System</c>，为一个字符串让通知模块依赖它，会把两个本来无关的模块绑在一起。
    /// </para>
    /// <para>
    /// 令牌以查询参数附在这个地址后面。落地页负责回显、确认、以及调
    /// <c>POST notifications/unsubscribe</c>；邮件服务商的一键退订则直接 POST 到
    /// <c>{ApiBaseUrl}/api/notifications/unsubscribe/one-click?token=…</c>（见 <see cref="OneClickEndpoint"/>）。
    /// </para>
    /// </remarks>
    public string? LandingUrl { get; set; }

    /// <summary>
    /// 一键退订端点的<b>绝对</b> URL，例如 <c>https://api.example.com/api/notifications/unsubscribe/one-click</c>。
    /// </summary>
    /// <remarks>
    /// ★ 与 <see cref="LandingUrl"/> <b>分开</b>是必须的：RFC 8058 的 <c>List-Unsubscribe-Post</c>
    /// 让邮件服务商<b>直接 POST</b> 到这个地址，收件人根本不会打开浏览器，所以它必须是 API 的
    /// 地址而不是前端页面的。两者同源的部署里它们只差一个路径，但那不能假设 ——
    /// 前后端分开部署是常态。
    /// <para>留空则只写 <c>List-Unsubscribe</c>（落地页链接），不声明一键 POST。</para>
    /// </remarks>
    public string? OneClickEndpoint { get; set; }
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
/// 传真发送配置选项（email-to-fax 网关）
/// </summary>
/// <remarks>
/// 网关把「传真号码 + 网关域名」当成一个邮箱地址收信，所以整条渠道只需要一个域名 ——
/// 没有账号、没有密钥，凭据是承载它的那套 SMTP 的。
/// </remarks>
public class FaxSenderOptions
{
    /// <summary>
    /// 获取或设置 是否启用传真渠道（默认 true）
    /// </summary>
    /// <remarks>
    /// 配了这一节就说明想用，所以默认为 true；这个开关是给「配置留着但这个环境先别发」用的。
    /// 关掉时回退到 <see cref="Services.UnconfiguredFaxSender"/>，也就是**发传真会失败**，
    /// 而不是安静地当作发过了。
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 获取或设置 网关域名，例如 <c>fax.example.com</c>（可带前导 <c>@</c>）
    /// </summary>
    public string GatewayDomain { get; set; } = string.Empty;

    /// <summary>
    /// Development override: when set, every outbound fax is delivered to this mailbox instead of
    /// the gateway. Configure via "Notification:FaxSender:DevOverrideEmail".
    /// </summary>
    /// <remarks>
    /// ★ 与 <see cref="MailSenderOptions.DevOverrideEmail"/> 分开是必要的：演示租户的传真号是假的，
    /// 而邮件通常仍要真发。原本的网关地址（含传真号码）会写进主题，所以在收件箱里看得出
    /// 这份传真本来要发给谁。重定向**不改变**发送路径本身，只换收件人。
    /// </remarks>
    public string? DevOverrideEmail { get; set; }

    /// <summary>
    /// 获取或设置 回执收件箱配置。**不配就是这个部署不收回执**，整条链一个后台线程都不起。
    /// </summary>
    public FaxConfirmationOptions? Confirmation { get; set; }
}

/// <summary>
/// 传真回执收件箱配置（IMAP）。
/// </summary>
/// <remarks>
/// <para>
/// email-to-fax 网关在拨号完成后（通常几分钟）回一封邮件说这份传真到没到。收下并判读它，
/// 一份没拨通的传真才不会永远显示成"已发送"。
/// </para>
/// <para>
/// ★ <b>整节可缺省</b>：<see cref="Host"/> 为空就当作"这个部署不收回执" —— 不报错、不起服务。
/// 回执是附加能力，没有它发传真一切照旧，所以它的缺省方向与
/// <see cref="FaxSenderOptions.GatewayDomain"/>（缺了就报错）刻意相反。
/// 但<b>填了一半要报错</b>：写了 <see cref="Host"/> 却漏了账号密码是笔误，不是"不想用"。
/// </para>
/// <para>
/// <b>建议用一个专用邮箱</b>：默认实现只取未读、处理完标已读，与人共用一个收件箱会互相把对方的信标掉。
/// </para>
/// </remarks>
public class FaxConfirmationOptions
{
    /// <summary>
    /// 获取或设置 是否启用回执收取（默认 true；配了收件箱就说明想用）。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 获取或设置 IMAP 主机。**留空 = 这个部署不收回执**。
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// 获取或设置 IMAP 端口（默认 993）
    /// </summary>
    public int Port { get; set; } = 993;

    /// <summary>
    /// 获取或设置 是否使用 SSL（默认 true）
    /// </summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>
    /// 获取或设置 IMAP 账号
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 获取或设置 IMAP 密码
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// 获取或设置 邮件夹（默认 <c>INBOX</c>）
    /// </summary>
    public string Folder { get; set; } = "INBOX";

    /// <summary>
    /// 获取或设置 轮询间隔秒数（默认 300 = 5 分钟）
    /// </summary>
    /// <remarks>
    /// 网关的回执本来就要几分钟才回，秒级轮询只是白白敲人家的 IMAP。下限 30 秒。
    /// </remarks>
    public int PollIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// 获取或设置 每轮最多处理多少封（默认 50）
    /// </summary>
    public int MaxMessagesPerPoll { get; set; } = 50;

    /// <summary>
    /// 获取或设置 按号码对号时往回看多少小时（默认 72）
    /// </summary>
    /// <remarks>
    /// ★ 号码是**不精确**的对号方式：同一个号码可能这个月发过好几份。窗口限制了认错的范围 ——
    /// 一份三天前的传真不会被今天的回执改掉。精确对号（<c>In-Reply-To</c> 指向承载邮件的
    /// Message-ID）不受这个窗口约束，因为它不会认错。
    /// </remarks>
    public int LookbackHours { get; set; } = 72;

    /// <summary>
    /// 获取或设置 处理后是否标记为已读（默认 true）
    /// </summary>
    /// <remarks>
    /// 这是默认实现避免重复处理的全部机制 —— 不必另建一张"处理过哪些邮件"的表，重启也不会重来。
    /// 关掉它就要自己保证幂等（<see cref="Services.IFaxConfirmationService"/> 本身是幂等的，
    /// 所以最坏后果只是每轮都白判读一遍同样的邮件）。
    /// </remarks>
    public bool MarkAsRead { get; set; } = true;

    /// <summary>
    /// 这一节到底配全了没有 —— <b>注册后台服务与配置校验共用这一个判据</b>。
    /// </summary>
    /// <remarks>
    /// ★ 两处各写一遍是这类开关最典型的漂移点：校验说"配好了"而注册说"没配"，
    /// 症状是配置完全正确、日志干净、回执就是不进来。
    /// </remarks>
    public bool IsUsable =>
        Enabled
        && !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(UserName)
        && !string.IsNullOrWhiteSpace(Password);
}

/// <summary>
/// Notification配置验证器
/// </summary>
public class NotificationOptionsValidator : OptionsValidatorBase<NotificationOptions>
{
    /// <summary>
    /// 邮箱地址的形状检查：本地部分 + <c>@</c> + 带点的域名，三段都不含空白。
    /// </summary>
    /// <remarks>
    /// 刻意宽松 —— 这里要挡的是配置写错（漏了域名、写了两个 <c>@</c>、粘进了空格），
    /// 不是判定一个地址收不收得到信，那只有真发一封才知道。
    /// </remarks>
    private const string EmailShape = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

    protected override void ValidateOptions(NotificationOptions options, List<string> errors)
    {
        // 验证各个配置部分
        ValidateMailSenderOptions(options.MailSender, errors);
        ValidateSmsSenderOptions(options.SmsSender, errors);
        ValidatePushSenderOptions(options.PushSender, errors);
        ValidateFaxSenderOptions(options.FaxSender, errors);
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
            !Regex.IsMatch(mailSender.FromEmail, EmailShape, RegexOptions.IgnoreCase))
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
    /// 验证传真发送配置
    /// </summary>
    /// <remarks>
    /// 网关域名写成邮箱地址（<c>fax@example.com</c>）是最容易犯的一个错，而它的后果是
    /// 拼出 <c>9055551234@fax@example.com</c> —— 一个投递失败的地址。这里当场拦掉。
    /// </remarks>
    private static void ValidateFaxSenderOptions(FaxSenderOptions? faxSender, List<string> errors)
    {
        if (faxSender == null || !faxSender.Enabled)
            return;

        var domain = faxSender.GatewayDomain?.Trim().TrimStart('@') ?? string.Empty;

        if (string.IsNullOrWhiteSpace(domain))
        {
            errors.Add("FaxSender.GatewayDomain is required when FaxSender.Enabled is true.");
        }
        else if (domain.Contains('@') || domain.Any(char.IsWhiteSpace) || !domain.Contains('.'))
        {
            errors.Add($"FaxSender.GatewayDomain '{faxSender.GatewayDomain}' must be a bare domain such as 'fax.example.com', not an email address.");
        }

        if (!string.IsNullOrWhiteSpace(faxSender.DevOverrideEmail) &&
            !Regex.IsMatch(faxSender.DevOverrideEmail, EmailShape, RegexOptions.IgnoreCase))
        {
            errors.Add("FaxSender.DevOverrideEmail must be a valid email address.");
        }

        ValidateFaxConfirmationOptions(faxSender.Confirmation, errors);
    }

    /// <summary>
    /// 验证传真回执收件箱配置
    /// </summary>
    /// <remarks>
    /// ★ <b>整节缺省是合法的</b>（这个部署不收回执），所以 <c>Host</c> 为空直接放行。
    /// 但填了 <c>Host</c> 就必须填全账号密码：那是笔误，而它的症状是"配了却不生效"——
    /// 没有报错、没有日志、回执就是不进来。宁可在启动时炸。
    /// </remarks>
    private static void ValidateFaxConfirmationOptions(FaxConfirmationOptions? confirmation, List<string> errors)
    {
        if (confirmation == null || !confirmation.Enabled || string.IsNullOrWhiteSpace(confirmation.Host))
            return;

        if (string.IsNullOrWhiteSpace(confirmation.UserName))
            errors.Add("FaxSender.Confirmation.UserName is required when a Host is configured.");

        if (string.IsNullOrWhiteSpace(confirmation.Password))
            errors.Add("FaxSender.Confirmation.Password is required when a Host is configured.");

        if (confirmation.Port is < 1 or > 65535)
            errors.Add($"FaxSender.Confirmation.Port '{confirmation.Port}' must be between 1 and 65535.");

        if (string.IsNullOrWhiteSpace(confirmation.Folder))
            errors.Add("FaxSender.Confirmation.Folder cannot be blank; the usual value is 'INBOX'.");

        // 网关的回执本来就要几分钟才回，秒级轮询只是白白敲人家的 IMAP（还可能被限流）。
        if (confirmation.PollIntervalSeconds < 30)
            errors.Add($"FaxSender.Confirmation.PollIntervalSeconds '{confirmation.PollIntervalSeconds}' is too small; 30 is the minimum.");

        if (confirmation.MaxMessagesPerPoll < 1)
            errors.Add($"FaxSender.Confirmation.MaxMessagesPerPoll '{confirmation.MaxMessagesPerPoll}' must be at least 1.");

        if (confirmation.LookbackHours < 1)
            errors.Add($"FaxSender.Confirmation.LookbackHours '{confirmation.LookbackHours}' must be at least 1.");
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

