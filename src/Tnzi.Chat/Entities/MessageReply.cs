namespace Tnzi.Chat.Entities;

/// <summary>
/// 消息回复实体
/// </summary>
public class MessageReply : FullAuditedEntity<Guid>
{
    /// <summary>
    /// 获取或设置 回复内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 回复人ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 获取或设置 所属主消息ID
    /// </summary>
    public Guid BelongMessageId { get; set; }

    /// <summary>
    /// 获取或设置 所属主消息
    /// </summary>
    public virtual Message BelongMessage { get; set; } = null!;

    /// <summary>
    /// 获取或设置 父回复ID（null 表示直接回复主消息）
    /// </summary>
    public Guid? ParentReplyId { get; set; }

    /// <summary>
    /// 获取或设置 父回复
    /// </summary>
    public virtual MessageReply? ParentReply { get; set; }

    /// <summary>
    /// 获取或设置 子回复集合
    /// </summary>
    public virtual ICollection<MessageReply> Replies { get; set; } = new List<MessageReply>();
}
