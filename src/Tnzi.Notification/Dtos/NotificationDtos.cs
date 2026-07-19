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

    public List<FileInfoDto> Attachments { get; set; } = null!;
    public bool SendImmediately { get; set; } = false;
    public int MaxRetryCount { get; set; } = 3;
    public string? TemplateName { get; set; }
    public string? LayoutName { get; set; }
    public Dictionary<string, object>? TemplateVariables { get; set; }
    public string? Category { get; set; }
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public Guid? SenderId { get; set; }

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
