namespace Tnzi.Chat.Entities;

/// <summary>
/// 消息级接收隔离：记录「某用户看不到某条消息」。当一条消息投递时，接收成员当下
/// 无 <c>chat.use</c> 权限（被禁用聊天），就为其写一行隔离记录；<c>GetMessages</c>/
/// <c>SearchMessages</c> 据此排除——于是被禁期间收到的（群）消息对其永久不可见，而
/// 禁用前 / 恢复后的消息完全正常。直聊无需此表：发给被禁用户的直聊消息在发送时即被
/// 拦截、根本不落库。表名 <c>Chat_MessageBlock</c>。
/// </summary>
public class MessageBlock : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>被隔离的消息。</summary>
    public Guid MessageId { get; set; }

    /// <summary>看不到该消息的用户。</summary>
    public Guid UserId { get; set; }

    /// <summary>导航属性——投递时消息 Id 尚未生成，经导航让 EF 在 SaveChanges 时回填 FK。</summary>
    public virtual ChatMessage? Message { get; set; }
}
