namespace Tnzi.Chat.Entities;

/// <summary>一条聊天消息（文本/图片/文件/系统）。软删支持撤回。</summary>
public class ChatMessage : MultiTenantAuditedEntity<Guid>
{
    public Guid ConversationId { get; set; }

    /// <summary>发送者；System 消息为 null。</summary>
    public Guid? SenderId { get; set; }

    public DateTime SentAt { get; set; }

    public MessageContentType ContentType { get; set; } = MessageContentType.Text;

    /// <summary>文本正文 / System 文本 / 图片文件可选图注。</summary>
    public string Content { get; set; } = string.Empty;

    [FileField]
    public string? FileId { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }

    /// <summary>可选标题（富系统通知的标题栏，如 "Order shipped"）。普通消息为 null。</summary>
    public string? Title { get; set; }

    /// <summary>可选点击跳转链接（富系统通知的 call-to-action，如订单详情 URL）。普通消息为 null。</summary>
    public string? LinkUrl { get; set; }

    /// <summary>可选分类标签（富系统通知的归类，如 "order" / "billing"）。普通消息为 null。</summary>
    public string? Category { get; set; }

    public virtual Conversation? Conversation { get; set; }
}
