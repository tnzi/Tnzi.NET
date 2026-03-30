namespace Tnzi.Chat.Dtos;

/// <summary>
/// 消息详情 DTO
/// </summary>
public class MessageDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public MessageType MessageType { get; set; }
    public Guid SenderId { get; set; }
    public string? SenderName { get; set; }
    public bool IsSent { get; set; }
    public bool IsDraft { get; set; }
    public bool CanReply { get; set; }
    public DateTime? BeginDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime CreationTime { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadTime { get; set; }
    public int ReplyCount { get; set; }
    public bool IsImportant { get; set; }
    public List<MessageReplyDto> Replies { get; set; } = new();
}

/// <summary>
/// 消息列表项 DTO（不含回复详情和完整内容）
/// </summary>
public class MessageListItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public MessageType MessageType { get; set; }
    public Guid SenderId { get; set; }
    public string? SenderName { get; set; }
    public bool CanReply { get; set; }
    public DateTime CreationTime { get; set; }
    public bool IsRead { get; set; }
    public int ReplyCount { get; set; }
    public bool IsImportant { get; set; }
}

/// <summary>
/// 创建消息 DTO
/// </summary>
public class CreateMessageDto
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    [Required]
    [MaxLength(4000)]
    public string Content { get; set; } = null!;

    public MessageType MessageType { get; set; }

    /// <summary>
    /// 接收角色ID列表（公共消息使用）
    /// </summary>
    public List<Guid>? RoleIds { get; set; }

    /// <summary>
    /// 接收用户ID列表（私人消息使用）
    /// </summary>
    public List<Guid>? RecipientIds { get; set; }

    public bool CanReply { get; set; } = true;
    public bool IsImportant { get; set; }
    public DateTime? BeginDate { get; set; }
    public DateTime? EndDate { get; set; }
}

/// <summary>
/// 更新消息 DTO
/// </summary>
public class UpdateMessageDto
{
    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(4000)]
    public string? Content { get; set; }

    public bool? CanReply { get; set; }
    public bool? IsImportant { get; set; }
    public DateTime? BeginDate { get; set; }
    public DateTime? EndDate { get; set; }
}

/// <summary>
/// 消息回复 DTO
/// </summary>
public class MessageReplyDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public Guid BelongMessageId { get; set; }
    public Guid? ParentReplyId { get; set; }
    public DateTime CreationTime { get; set; }
    public List<MessageReplyDto> Replies { get; set; } = new();
}

/// <summary>
/// 创建消息回复 DTO
/// </summary>
public class CreateMessageReplyDto
{
    public Guid MessageId { get; set; }

    [Required]
    [MaxLength(4000)]
    public string Content { get; set; } = null!;

    /// <summary>
    /// 父回复ID（回复某个回复时指定）
    /// </summary>
    public Guid? ParentReplyId { get; set; }
}

/// <summary>
/// 消息查询 DTO
/// </summary>
public class MessageQueryDto : PagedQueryDto
{
    public MessageType? MessageType { get; set; }
    public bool? IsRead { get; set; }
    public bool? IsImportant { get; set; }
    public string? Keyword { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

/// <summary>
/// 消息统计 DTO
/// </summary>
public class ChatStatisticsDto
{
    /// <summary>
    /// 总消息数
    /// </summary>
    public int TotalMessages { get; set; }

    /// <summary>
    /// 已发送消息数
    /// </summary>
    public int SentMessages { get; set; }

    /// <summary>
    /// 草稿消息数
    /// </summary>
    public int DraftMessages { get; set; }

    /// <summary>
    /// 公共消息数
    /// </summary>
    public int PublicMessages { get; set; }

    /// <summary>
    /// 私人消息数
    /// </summary>
    public int PrivateMessages { get; set; }

    /// <summary>
    /// 总回复数
    /// </summary>
    public int TotalReplies { get; set; }

    /// <summary>
    /// 活跃发送者数量（去重）
    /// </summary>
    public int ActiveSenders { get; set; }

    /// <summary>
    /// 重要消息数
    /// </summary>
    public int ImportantMessages { get; set; }
}

/// <summary>
/// 管理端消息查询 DTO
/// </summary>
public class AdminMessageQueryDto : PagedQueryDto
{
    public MessageType? MessageType { get; set; }
    public bool? IsSent { get; set; }
    public bool? IsDraft { get; set; }
    public Guid? SenderId { get; set; }
    public string? Keyword { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
