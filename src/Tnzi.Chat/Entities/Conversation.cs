namespace Tnzi.Chat.Entities;

/// <summary>
/// 会话：单聊(Direct)/群聊(Group)/系统通知(System)。
/// </summary>
public class Conversation : MultiTenantAuditedEntity<Guid>
{
    public ConversationType Type { get; set; }

    /// <summary>群名；Direct 为 null（前端派生为对方名）；System 语义为系统通知。</summary>
    public string? Title { get; set; }

    /// <summary>
    /// 群头像文件 id（可选，接 Storage）。
    /// <c>Public = true</c>：会话列表 / 聊天窗以匿名 <c>&lt;img src&gt;</c> 渲染它，
    /// 故写入即由框架标记该文件公开可读（详见 <see cref="FileFieldAttribute.Public"/>）。
    /// </summary>
    [FileField(Public = true)]
    public string? AvatarFileId { get; set; }

    /// <summary>群主（Group）；Direct/System 为 null。</summary>
    public Guid? OwnerId { get; set; }

    /// <summary>群公告（群主可编辑，成员可见）。</summary>
    public string? Notice { get; set; }

    /// <summary>幂等键：Direct="{minId:N}:{maxId:N}"，System="system:{userId:N}"，Group=null。</summary>
    public string? DirectKey { get; set; }

    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public int MemberCount { get; set; }

    public virtual ICollection<ConversationMember> Members { get; set; } = new List<ConversationMember>();
}
