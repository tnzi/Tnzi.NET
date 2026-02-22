namespace Tnzi.Notification.Entities;

/// <summary>
/// 消息接收者
/// </summary>
public class Recipient : EntityBase<Guid>
{
    /// <summary>
    /// 消息ID
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// 消息实体
    /// </summary>
    public virtual Message Message { get; set; } = null!;

    /// <summary>
    /// 接收者地址（邮箱/手机号/设备Token）
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// 接收者名称（可选）
    /// </summary>
    public string? Name { get; set; }

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
    /// 外部服务返回的消息ID（用于追踪）
    /// </summary>
    public string? ExternalMessageId { get; set; }

    /// <summary>
    /// 关联系统用户ID（用于站内通知收件箱）
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 已读状态
    /// </summary>
    public bool IsRead { get; set; }

    /// <summary>
    /// 已读时间
    /// </summary>
    public DateTime? ReadTime { get; set; }
}
