namespace Tnzi.Chat.Entities;

/// <summary>一次广播（系统通知）的发送记录。表名 `Chat_BroadcastLog`。</summary>
public class BroadcastLog : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>发送者（管理员）用户 ID。程序化（业务模块）发送时为 null。</summary>
    public Guid? SenderId { get; set; }

    /// <summary>
    /// 来源标识：管理端 UI 广播为 null（由 <see cref="SenderId"/> 标识发送人）；
    /// 业务模块经 <c>NotifyUsersAsync</c>/<c>NotifyRoleAsync</c> 程序化发送时记录调用方标签（如 "OrderModule"/"order.shipped"），供审计溯源。
    /// </summary>
    public string? Source { get; set; }

    /// <summary>广播内容。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>目标类型（All/Roles/Users）。</summary>
    public BroadcastTargetType TargetType { get; set; }

    /// <summary>目标摘要（如 "All users" / "2 role(s)" / "5 user(s)"），供列表展示。</summary>
    public string? TargetSummary { get; set; }

    /// <summary>实际投递到的用户数。</summary>
    public int RecipientCount { get; set; }
}
