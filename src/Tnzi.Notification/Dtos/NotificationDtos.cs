namespace Tnzi.Notification.Dtos;

/// <summary>
/// 创建通知请求
/// </summary>
public class CreateNotificationRequest
{
    public NotificationType Type { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsHtml { get; set; } = true;

    [Required]
    [MinLength(1, ErrorMessage = "At least one recipient is required.")]
    public List<RecipientInput> Recipients { get; set; } = null!;

    /// <summary>
    /// 附件（可选）。必须声明为可空：不可空的引用类型会被 ASP.NET Core 的模型验证
    /// 隐式当成必填，调用方即使不带附件也得传一个空数组，而服务端本就按可空处理
    /// （CreateAsync 里是 request.Attachments?.Select(...)）。
    /// </summary>
    public List<FileInfoDto>? Attachments { get; set; }
    public bool SendImmediately { get; set; } = false;
    public int MaxRetryCount { get; set; } = 3;
    public string? TemplateName { get; set; }
    public string? LayoutName { get; set; }
    public Dictionary<string, object>? TemplateVariables { get; set; }
    public string? Category { get; set; }
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public Guid? SenderId { get; set; }

    /// <summary>
    /// 服务商键：由该渠道上哪一个具名发送器投递（<c>Notification:MailSenders:{key}</c> 这类配置节，
    /// 或代码里注册的同键 keyed sender）。留空 = 交给 <c>INotificationProviderSelector</c>，
    /// 它也不选时走默认发送器。
    /// </summary>
    /// <remarks>
    /// 指定了就必须是已注册的键，否则创建直接 400 —— 不会静默换一家发出去。
    /// </remarks>
    public string? ProviderKey { get; set; }

    /// <summary>
    /// 事务性消息（与商业/群发消息相对）：本条消息不受退订名单约束。默认 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// 密码重置、二次验证码、账单与订阅通知设为 <c>true</c> —— 退订按钮管的是营销邮件，
    /// 不该让人再也收不到验证码。**拿不准就别设**：默认按商业消息处理，宁可少发一条，
    /// 也不要让收件人点过的退订形同虚设。
    /// </remarks>
    public bool IsTransactional { get; set; }

    /// <summary>
    /// Scheduled send time (null = send immediately or as queued).
    /// When set, notification will be held until this UTC time.
    /// </summary>
    public DateTime? ScheduledTime { get; set; }
}

/// <summary>
/// 接收者输入（创建通知时使用）
/// </summary>
public class RecipientInput
{
    public string Address { get; set; } = string.Empty;
    public string? Name { get; set; }
    public Guid? UserId { get; set; }
}

/// <summary>
/// 接收者输出（查询通知时使用）
/// </summary>
public class RecipientOutput
{
    public Guid Id { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? Name { get; set; }
    public NotificationStatus Status { get; set; }
    public DateTime? SentTime { get; set; }
    public string? FailureReason { get; set; }
    public string? ExternalMessageId { get; set; }
    public Guid? UserId { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadTime { get; set; }

    /// <summary>
    /// 因收件人本人的静默时段而延后到这一刻再投递（UTC）。<see langword="null"/> = 没有被延后。
    /// </summary>
    /// <remarks>
    /// ★ 投递报告要分得清「还没送到因为失败了」与「还没送到因为对方设了免打扰」。
    /// 后者不写 <see cref="FailureReason"/>（那不是一次失败），线索就在这一列。
    /// <para>
    /// ★★ <b>要与 <see cref="Status"/> 一起读。</b>这一列在收件人真的发出去之后<b>仍然保留</b>，
    /// 作为「这一条曾被推迟到几点」的记录 —— 所以它有值不代表还在等。
    /// 「仍在等」的判据是 <c>Status == Scheduled</c> 且这一列尚未到期。
    /// </para>
    /// </remarks>
    public DateTime? DeferredUntil { get; set; }
}

/// <summary>
/// 通知信息（查询结果）
/// </summary>
public class NotificationInfo
{
    public Guid Id { get; set; }
    public NotificationType Type { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsHtml { get; set; }
    public NotificationStatus Status { get; set; }
    public DateTime? SentTime { get; set; }
    public string? FailureReason { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetryCount { get; set; }
    public int TotalRecipientCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public DateTime CreationTime { get; set; }
    public NotificationPriority Priority { get; set; }
    public Guid? SenderId { get; set; }
    public string Category { get; set; } = "General";

    /// <summary>事务性消息：不受退订名单约束。见 <see cref="CreateNotificationRequest.IsTransactional"/>。</summary>
    public bool IsTransactional { get; set; }

    public string? TemplateName { get; set; }

    /// <summary>投递这条消息的服务商键；<see langword="null"/> = 默认发送器。</summary>
    public string? ProviderKey { get; set; }

    /// <summary>
    /// 最早几点发（UTC）：<c>Scheduled</c> 消息上是定时的时刻；<c>Pending</c> 消息上是交给发送（排队）或下一次重试到期的时刻；
    /// <c>Pending</c> 且为 <see langword="null"/> 是只创建、没要求发送的草稿。
    /// </summary>
    public DateTime? ScheduledTime { get; set; }

    /// <summary>
    /// 收件人。★ 只有按 Id 取详情（以及创建 / 失败列表）时带；消息列表与已排期列表的行恒为空集合：
    /// 列表只用 <see cref="SuccessCount"/> / <see cref="TotalRecipientCount"/>，「哪些人、为什么」看投递报告。
    /// </summary>
    public List<RecipientOutput> Recipients { get; set; } = new();

    /// <summary>附件。与 <see cref="Recipients"/> 同：列表行恒为空集合。</summary>
    public List<FileInfoDto> Attachments { get; set; } = new();
}

/// <summary>
/// 查询通知请求
/// </summary>
public class QueryNotificationRequest : PagedQueryDto
{
    public NotificationType? Type { get; set; }
    public NotificationStatus? Status { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? Keyword { get; set; }

    /// <summary>
    /// Filter by category (e.g., "General", "Marketing", "System")
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Filter by priority
    /// </summary>
    public NotificationPriority? Priority { get; set; }

    /// <summary>
    /// Filter by sender user ID
    /// </summary>
    public Guid? SenderId { get; set; }
}

/// <summary>
/// 预览通知渲染结果的请求：渲染要用的那几项，收件人可选。
/// </summary>
/// <remarks>
/// <para>
/// 与创建通知的 <see cref="CreateNotificationRequest"/> 刻意分开：创建要求至少一个收件人，而预览渲染的是内容，
/// 管理端按模板名 + 变量预览时没有任何地址可填。此前预览直接收创建请求，模板页发的 <c>recipients: []</c>
/// 每一次都被模型验证答 400，预览从来没有工作过。
/// </para>
/// <para>
/// 字段名与创建请求逐字相同：把一份完整的创建请求原样发来预览照样可以，多出来的字段被忽略。
/// </para>
/// </remarks>
public class PreviewNotificationRequest
{
    public NotificationType Type { get; set; }

    /// <summary>主题。给了模板时是模板渲染不出来时的回落内容，不给就把渲染失败如实报出来。</summary>
    public string? Subject { get; set; }

    /// <summary>正文。与 <see cref="Subject"/> 同理。</summary>
    public string? Content { get; set; }

    public bool IsHtml { get; set; } = true;
    public string? TemplateName { get; set; }
    public string? LayoutName { get; set; }
    public Dictionary<string, object>? TemplateVariables { get; set; }
    public string? Category { get; set; }

    /// <summary>
    /// 可选。给了就按创建通知的同一条规则校验地址，并在 <see cref="NotificationPreviewDto.RecipientCount"/> 里回报人数；不给时人数为 0。
    /// </summary>
    public List<RecipientInput>? Recipients { get; set; }
}

/// <summary>
/// 通知预览结果
/// </summary>
public class NotificationPreviewDto
{
    /// <summary>Rendered subject</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Rendered content (HTML or plain text)</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Whether content is HTML</summary>
    public bool IsHtml { get; set; }

    /// <summary>Resolved category</summary>
    public string Category { get; set; } = "General";

    /// <summary>Recipient count</summary>
    public int RecipientCount { get; set; }

    /// <summary>Template used (null if no template)</summary>
    public string? TemplateName { get; set; }
}

/// <summary>
/// 消息投递报告
/// </summary>
public class DeliveryReportDto
{
    /// <summary>Message ID</summary>
    public Guid MessageId { get; set; }

    /// <summary>Message subject</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Notification type</summary>
    public NotificationType Type { get; set; }

    /// <summary>Total recipients</summary>
    public int TotalRecipients { get; set; }

    /// <summary>Successfully sent</summary>
    public int SentCount { get; set; }

    /// <summary>Failed count</summary>
    public int FailedCount { get; set; }

    /// <summary>Pending count</summary>
    public int PendingCount { get; set; }

    /// <summary>Read count (for in-app notifications)</summary>
    public int ReadCount { get; set; }

    /// <summary>Delivery success rate (0-1)</summary>
    public double SuccessRate { get; set; }

    /// <summary>
    /// 收件人明细：最多 200 行，失败的排最前，其次被拦下的（<c>Cancelled</c>），然后其余。
    /// </summary>
    /// <remarks>
    /// 上面的计数始终覆盖全部收件人；行数小于 <see cref="TotalRecipients"/> 即明细被截断。完整名单走按 Id 取详情。
    /// </remarks>
    public List<RecipientOutput> Recipients { get; set; } = [];
}
