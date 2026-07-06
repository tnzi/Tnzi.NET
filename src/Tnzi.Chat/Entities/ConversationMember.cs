namespace Tnzi.Chat.Entities;

/// <summary>会话成员 + 每会话已读水位/未读计数/静音。</summary>
public class ConversationMember : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public MemberRole Role { get; set; } = MemberRole.Member;

    public DateTime? LastReadAt { get; set; }

    /// <summary>去规范化未读数：收他人消息 +1，标记已读清 0。</summary>
    public int UnreadCount { get; set; }

    public bool IsMuted { get; set; }

    /// <summary>本人维度置顶会话。</summary>
    public bool IsSticky { get; set; }

    /// <summary>
    /// 本人维度隐藏会话（不出现在会话列表）；收到新消息时服务端自动置回 false 重新浮现。
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>清空记录水位：仅本人 GetMessages/Search 过滤掉 SentAt &lt;= ClearedAt 的消息。</summary>
    public DateTime? ClearedAt { get; set; }

    /// <summary>Direct=对对方的备注名；Group=我对该群的备注名（仅本人可见）。</summary>
    public string? Remark { get; set; }

    /// <summary>Group=我在本群的群昵称（My Alias in Group）。</summary>
    public string? Alias { get; set; }

    /// <summary>null=在群；退群/被移除=软移除（保留历史）。</summary>
    public DateTime? RemovedAt { get; set; }

    public virtual Conversation? Conversation { get; set; }
}
