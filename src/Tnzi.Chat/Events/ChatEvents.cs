namespace Tnzi.Chat.Events;

/// <summary>
/// 消息发送事件
/// </summary>
public class MessageSentEvent : EventBase
{
    /// <summary>
    /// 消息ID
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// 发送人ID
    /// </summary>
    public Guid SenderId { get; set; }

    /// <summary>
    /// 消息类型
    /// </summary>
    public MessageType MessageType { get; set; }

    /// <summary>
    /// 收件人ID列表
    /// </summary>
    public List<Guid> RecipientUserIds { get; set; } = new();

    /// <summary>
    /// 消息标题
    /// </summary>
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// 消息已读事件
/// </summary>
public class MessageReadEvent : EventBase
{
    /// <summary>
    /// 消息ID
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// 阅读人ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 消息发送人ID（用于推送已读回执）
    /// </summary>
    public Guid SenderId { get; set; }
}

/// <summary>
/// 消息回复事件
/// </summary>
public class MessageRepliedEvent : EventBase
{
    /// <summary>
    /// 回复ID
    /// </summary>
    public Guid ReplyId { get; set; }

    /// <summary>
    /// 所属消息ID
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// 回复人ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 需要通知的用户ID列表
    /// </summary>
    public List<Guid> NotifyUserIds { get; set; } = new();

    /// <summary>
    /// 回复内容
    /// </summary>
    public string Content { get; set; } = string.Empty;
}
