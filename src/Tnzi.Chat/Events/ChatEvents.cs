namespace Tnzi.Chat.Events;

public enum ConversationChangeType
{
    Created = 1,
    MemberAdded = 2,
    MemberRemoved = 3,
    Left = 4,
    Renamed = 5,
    Dissolved = 6
}

/// <summary>新消息发出（推给除发送者外所有在群成员）。</summary>
public class ConversationMessageSentEvent : EventBase
{
    public Guid ConversationId { get; set; }
    public Guid MessageId { get; set; }
    public Guid? SenderId { get; set; }
    public MessageContentType ContentType { get; set; }
    public string Preview { get; set; } = string.Empty;
    public List<Guid> RecipientUserIds { get; set; } = new();

    /// <summary>完整消息体，供实时推送给接收方直接增量追加（无需回拉）。</summary>
    public ChatMessageDto? Message { get; set; }
}

/// <summary>某成员标记会话已读（推给其他成员做已读回执）。</summary>
public class ConversationReadEvent : EventBase
{
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ReadAt { get; set; }
    public List<Guid> OtherMemberIds { get; set; } = new();
}

/// <summary>会话成员/属性变化（建群/加退人/改名/解散）。</summary>
public class ConversationChangedEvent : EventBase
{
    public Guid ConversationId { get; set; }
    public ConversationChangeType ChangeType { get; set; }
    public List<Guid> AffectedUserIds { get; set; } = new();
}
