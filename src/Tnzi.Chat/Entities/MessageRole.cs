namespace Tnzi.Chat.Entities;

/// <summary>
/// 消息接收角色（公共消息的目标角色）
/// </summary>
public class MessageRole : EntityBase<Guid>
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
    /// 获取或设置 角色ID
    /// </summary>
    public Guid RoleId { get; set; }
}
