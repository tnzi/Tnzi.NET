
namespace Tnzi.Notification.Entities;

/// <summary>
/// 消息实体
/// </summary>
public class Message : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 通知类型（Email, SMS, Push）
    /// </summary>
    public NotificationType Type { get; set; }

    /// <summary>
    /// 主题/标题
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// 内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 是否HTML格式（仅Email）
    /// </summary>
    public bool IsHtml { get; set; }

    /// <summary>
    /// 发送状态
    /// </summary>
    public NotificationStatus Status { get; set; }

    /// <summary>
    /// 发送时间
    /// </summary>
    public DateTime? SentTime { get; set; }

    /// <summary>
    /// 失败原因
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// 重试次数
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// 总接收者数量
    /// </summary>
    public int TotalRecipientCount { get; set; }

    /// <summary>
    /// 成功发送数量
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 失败数量
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// 消息接收者列表
    /// </summary>
    public virtual ICollection<Recipient> Recipients { get; set; } = new List<Recipient>();

    /// <summary>
    /// 附件列表（仅Email）
    /// </summary>
    public virtual ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

    /// <summary>
    /// 优先级
    /// </summary>
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

    /// <summary>
    /// 发送者ID
    /// </summary>
    public Guid? SenderId { get; set; }

    /// <summary>
    /// 分类/标签
    /// </summary>
    public string Category { get; set; } = "General";

    /// <summary>
    /// 事务性消息（与商业/群发消息相对）：本条消息不受退订名单约束。
    /// </summary>
    /// <remarks>
    /// 密码重置、二次验证码、账单与订阅通知属于此类 —— 它们是对方主动发起或既有关系
    /// 必需的往来，退订按钮管的是营销邮件，不该让人再也收不到验证码。
    /// <b>默认 false</b>：拿不准就按商业消息处理，宁可少发一条也不要把退订当摆设。
    /// 必须落库而不是只留在请求里：定时与排队的消息在很久以后才真正发送，那时只剩这条记录。
    /// </remarks>
    public bool IsTransactional { get; set; }

    /// <summary>
    /// 模板名称（如果使用模板创建）
    /// </summary>
    public string? TemplateName { get; set; }

    /// <summary>
    /// Scheduled send time (null = send immediately or as queued)
    /// When set, notification will not be sent until this time
    /// </summary>
    public DateTime? ScheduledTime { get; set; }
}
