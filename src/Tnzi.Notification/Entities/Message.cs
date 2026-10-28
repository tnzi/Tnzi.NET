
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
    /// 重试次数：失败之后经 <c>INotificationRetryService.RetryAsync</c>（单条或「重试全部失败」）重新发送了几次。
    /// 首次发送、派发恢复续发与静默时段延后到点的那一遍都不算。
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// 最大重试次数：<see cref="RetryCount"/> 到了这个数，就不再接受重试。
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
    /// 附件列表（<see cref="NotificationType.Email"/> 与 <see cref="NotificationType.Fax"/>）
    /// </summary>
    /// <remarks>
    /// 传真的附件不是"附带的"：一份传真的全部内容就是那一个 PDF，正文不参与投递。
    /// 所以 <c>Type = Fax</c> 的消息必须<b>恰好一个</b> PDF 附件，多份先合并成一份
    /// （<c>Tnzi.Documents</c> 的 <c>IPdfCombiner</c>）。
    /// </remarks>
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
    /// 服务商键：这条消息由该渠道上哪一个具名发送器投递；<see langword="null"/> = 默认发送器。
    /// </summary>
    /// <remarks>
    /// 必须落库而不是只留在请求里：排队、定时、重试、重启续发都从这条记录出发，同一条消息的
    /// 每一次投递尝试都要走同一家。创建时由调用方显式指定，或由 <c>INotificationProviderSelector</c>
    /// 按业务规则选出；两者都经 <c>NotificationProviderKeys.Normalize</c> 收口，「默认」只有 <see langword="null"/> 一种写法。
    /// </remarks>
    public string? ProviderKey { get; set; }

    /// <summary>
    /// 这条消息最早几点发（UTC）。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>Scheduled</c> 消息：调用方定下的发送时刻。</item>
    /// <item><c>Pending</c> 消息：交给发送的那一刻（排队发送），或下一次重试到期的时刻（延迟重试）。</item>
    /// <item><c>Pending</c> 且为 <see langword="null"/>：只创建、没要求发送的草稿。</item>
    /// </list>
    /// ★ 后两条是派发恢复的判据：排队与重试真正会去发它的只有进程内的队列与定时器，进程一停就没了；
    /// <c>Pending</c> + 这一列是「已经交给发送」的标记，超过宽限仍停在 <c>Pending</c> 由恢复扫描接手，草稿永远不碰。
    /// </remarks>
    public DateTime? ScheduledTime { get; set; }
}
