namespace Tnzi.Chat.Entities;

/// <summary>
/// 消息接收记录实体
/// </summary>
public class MessageReceive : EntityBase<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 获取或设置 消息ID
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// 获取或设置 消息
    /// </summary>
    public virtual Message Message { get; set; } = null!;

    /// <summary>
    /// 获取或设置 接收人用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 获取或设置 阅读时间（null 表示未读）
    /// </summary>
    public DateTime? ReadTime { get; set; }
}
