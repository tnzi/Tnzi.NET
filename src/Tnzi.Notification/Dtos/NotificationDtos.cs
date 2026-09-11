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
    public DateTime? ScheduledTime { get; set; }
    public List<RecipientOutput> Recipients { get; set; } = new();
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

    /// <summary>Recipient details</summary>
    public List<RecipientOutput> Recipients { get; set; } = [];
}
