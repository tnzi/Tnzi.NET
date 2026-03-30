namespace Tnzi.Chat.Dtos;

/// <summary>
/// 保存草稿 DTO
/// </summary>
public class SaveDraftDto
{
    /// <summary>
    /// 草稿ID（更新已有草稿时提供）
    /// </summary>
    public Guid? Id { get; set; }

    /// <summary>
    /// 标题
    /// </summary>
    [MaxLength(200)]
    public string? Title { get; set; }

    /// <summary>
    /// 内容
    /// </summary>
    [Required]
    [MaxLength(4000)]
    public string Content { get; set; } = null!;

    /// <summary>
    /// 接收用户ID列表（私人消息）
    /// </summary>
    public List<Guid>? RecipientIds { get; set; }

    /// <summary>
    /// 接收角色ID列表（公共消息）
    /// </summary>
    public List<Guid>? RoleIds { get; set; }

    /// <summary>
    /// 消息类型
    /// </summary>
    public MessageType? MessageType { get; set; }

    /// <summary>
    /// 是否允许回复
    /// </summary>
    public bool CanReply { get; set; } = true;

    /// <summary>
    /// 是否重要
    /// </summary>
    public bool IsImportant { get; set; }
}
