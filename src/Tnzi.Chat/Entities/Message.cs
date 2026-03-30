namespace Tnzi.Chat.Entities;

/// <summary>
/// 消息类型
/// </summary>
public enum MessageType
{
    /// <summary>
    /// 公共消息（发送给角色）
    /// </summary>
    Public = 1,

    /// <summary>
    /// 私人消息（发送给指定用户）
    /// </summary>
    Private = 2
}

/// <summary>
/// 消息实体
/// </summary>
public class Message : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 消息类型
    /// </summary>
    public MessageType MessageType { get; set; }

    /// <summary>
    /// 获取或设置 发送人ID
    /// </summary>
    public Guid SenderId { get; set; }

    /// <summary>
    /// 获取或设置 是否已发送
    /// </summary>
    public bool IsSent { get; set; }

    /// <summary>
    /// 获取或设置 是否为草稿
    /// </summary>
    public bool IsDraft { get; set; }

    /// <summary>
    /// 获取或设置 是否允许回复
    /// </summary>
    public bool CanReply { get; set; } = true;

    /// <summary>
    /// 获取或设置 生效时间
    /// </summary>
    public DateTime? BeginDate { get; set; }

    /// <summary>
    /// 获取或设置 过期时间
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Whether this message is marked as important/priority
    /// </summary>
    public bool IsImportant { get; set; }

    /// <summary>
    /// 获取或设置 接收记录集合
    /// </summary>
    public virtual ICollection<MessageReceive> Receives { get; set; } = new List<MessageReceive>();

    /// <summary>
    /// 获取或设置 回复集合
    /// </summary>
    public virtual ICollection<MessageReply> Replies { get; set; } = new List<MessageReply>();

    /// <summary>
    /// 获取或设置 收件人集合（私人消息）
    /// </summary>
    public virtual ICollection<MessageRecipient> Recipients { get; set; } = new List<MessageRecipient>();

    /// <summary>
    /// 获取或设置 接收角色集合（公共消息）
    /// </summary>
    public virtual ICollection<MessageRole> Roles { get; set; } = new List<MessageRole>();
}
