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
    /// 获取或设置 邮件发送配置（<b>默认</b>发送器；没带 <c>ProviderKey</c> 的消息走这里）
    /// </summary>
    public MailSenderOptions? MailSender { get; set; }

    /// <summary>
    /// 获取或设置 <b>具名</b>邮件发送配置：键 = 服务商键，值 = 与 <see cref="MailSender"/> 同形的一节。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 消息用 <c>CreateNotificationRequest.ProviderKey</c> 指定走哪一个；一条渠道要接两家服务商
    /// （验证码走一家、营销走另一家）就在这里各配一节。键不区分大小写，
    /// 且<b>不能是 <c>default</c></b>（那个键永远指 <see cref="MailSender"/>）。
    /// </para>
    /// <para>
    /// ★ 只配了具名节、没配 <see cref="MailSender"/> 时，默认发送器<b>不是</b>开发期那个报成功的
    /// <c>NullEmailSender</c>，而是当场失败的 <c>UnconfiguredEmailSender</c>：一个显然要发信的部署里，
    /// 没带键的消息被静默吞掉毫无症状。要么补上默认节，要么在代码里注册默认的 <c>IEmailSender</c>。
    /// </para>
    /// </remarks>
    public Dictionary<string, MailSenderOptions> MailSenders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 获取或设置 短信发送配置（<b>默认</b>发送器）
    /// </summary>
    public SmsSenderOptions? SmsSender { get; set; }

    /// <summary>
    /// 获取或设置 <b>具名</b>短信发送配置。规则与 <see cref="MailSenders"/> 相同。
    /// </summary>
    public Dictionary<string, SmsSenderOptions> SmsSenders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 获取或设置 Push推送配置（<b>默认</b>发送器）
    /// </summary>
    public PushSenderOptions? PushSender { get; set; }

    /// <summary>
    /// 获取或设置 <b>具名</b>推送发送配置。规则与 <see cref="MailSenders"/> 相同；
    /// 实现同样住在可选的 <c>Tnzi.Notification.Push</c> 子模块里，没加载时每个具名键都失败并指名要加载什么。
    /// </summary>
    public Dictionary<string, PushSenderOptions> PushSenders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 获取或设置 传真发送配置（email-to-fax 网关；<b>默认</b>发送器）
    /// </summary>
    public FaxSenderOptions? FaxSender { get; set; }

    /// <summary>
    /// 获取或设置 <b>具名</b>传真发送配置。规则与 <see cref="MailSenders"/> 相同；
    /// 每一节可以用 <see cref="FaxSenderOptions.EmailProviderKey"/> 指定承载它的邮件发送器。
    /// </summary>
    public Dictionary<string, FaxSenderOptions> FaxSenders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// 获取或设置 附件来源配置
    /// </summary>
    public AttachmentOptions Attachments { get; set; } = new();
}

/// <summary>
/// 附件来源配置：按路径 / URL 取件的附件允许来自哪里。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>为什么要有这一节。</b>持久化的附件只存 <c>FilePath</c>（本地路径或 URL），字节在发信那一刻才取；
/// 而 <c>FilePath</c> 可以从管理端请求体原样进来，收件人地址在同一个请求体里。没有来源约束时，
/// 持 <c>notification.message.create</c> 的人一次请求就能把服务端任意文件（生产配置、密钥）寄到任意邮箱，
/// 或对内网 / 云元数据端点做一次带回显的 SSRF —— 日志只记一次正常投递。
/// </para>
/// <para>
/// ★ <b>缺省是关的。</b><see cref="AllowedLocalRoots"/> 默认为空 = 任何本地路径都拒绝；远程 URL 只放行
/// http/https 且必须过 <c>EgressGuard</c>（拒 loopback / RFC1918 / 链路本地 / 云元数据）。
/// 传本地路径的服务端调用方要么把那个目录列进来，要么改传字节（<c>EmailAttachment.FromBytes</c>）。
/// </para>
/// </remarks>
[ConfigSection("Notification:Attachments")]
public class AttachmentOptions
{
    /// <summary>
    /// 获取或设置 允许作为附件来源的本地根目录（绝对路径）。空 = 拒绝一切本地路径。
    /// </summary>
    /// <remarks>
    /// 路径先 <c>Path.GetFullPath</c> 归一化再做前缀比对（<c>..</c> 逃逸因此无效）；
    /// 文件存在且是符号链接时，链接的最终目标也必须在某个根之下。
    /// </remarks>
    public string[] AllowedLocalRoots { get; set; } = [];

    /// <summary>
    /// 获取或设置 是否允许按 http/https URL 取远程附件（默认 true）。
    /// </summary>
    /// <remarks>
    /// 开着时每个 URL 仍要过 <c>EgressGuard</c>：其它 scheme 与私网 / 链路本地 / loopback 一律拒绝，
    /// 且创建与发信两刻各查一次（落库后 DNS 可能已变）。关掉后所有远程 URL 拒绝。
    /// </remarks>
    public bool AllowRemoteUrls { get; set; } = true;

    /// <summary>
    /// 获取或设置 单个按路径 / URL 取件的附件的字节上限（默认 25 MB）。
    /// </summary>
    /// <remarks>
    /// 是闸门不是调优项：没有它，一个指向几个 GB 的 URL 就能把发送进程的内存打满。
    /// 内存附件（<c>EmailAttachment.Content</c>）不受此限 —— 那些字节已经在调用方手里了。
    /// </remarks>
    public long MaxAttachmentBytes { get; set; } = 25L * 1024 * 1024;
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
    /// 获取或设置 承载这条传真通道的邮件发送器的服务商键；留空 = 默认的 <c>IEmailSender</c>。
    /// </summary>
    /// <remarks>
    /// 传真经 email-to-fax 网关投递，本质是一封发往网关的邮件。网关通常要求信从某个特定的
    /// 发件账号发出（它按发件人认账号），而那个账号未必是应用发普通邮件的那一个 ——
    /// 于是这里可以指向 <c>Notification:MailSenders</c> 里的某一节（或代码里注册的同键 keyed
    /// <c>IEmailSender</c>）。指向一个不存在的键时，解析 <c>IFaxSender</c> 就抛 <c>ConfigurationException</c>，
    /// <b>不会退回默认发送器</b>：退回去的信网关不认，症状是「传真发成功了但永远没到」。
    /// </remarks>
    public string? EmailProviderKey { get; set; }

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

