namespace Tnzi.Chat.Entities;

/// <summary>一次广播（系统通知）的发送记录。表名 `Chat_BroadcastLog`。</summary>
public class BroadcastLog : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>发送者（管理员）用户 ID。</summary>
    public Guid? SenderId { get; set; }

    /// <summary>广播内容。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>目标类型（All/Roles/Users）。</summary>
    public BroadcastTargetType TargetType { get; set; }

    /// <summary>目标摘要（如 "All users" / "2 role(s)" / "5 user(s)"），供列表展示。</summary>
    public string? TargetSummary { get; set; }

    /// <summary>实际投递到的用户数。</summary>
    public int RecipientCount { get; set; }
}
