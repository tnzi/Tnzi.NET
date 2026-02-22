namespace Tnzi.Chat.Entities;

/// <summary>
/// 消息收件人（私人消息的目标用户）
/// </summary>
public class MessageRecipient : EntityBase<Guid>
{
    /// <summary>
    /// 获取或设置 消息ID
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// 获取或设置 消息
    /// </summary>
    public virtual Message Message { get; set; } = null!;

    /// <summary>
    /// 获取或设置 收件人用户ID
    /// </summary>
    public Guid UserId { get; set; }
}
