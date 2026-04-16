namespace Tnzi.Chat.Dtos;

/// <summary>
/// Chat session detail DTO (read model).
/// </summary>
public class ChatSessionDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ChatSessionStatus Status { get; set; }

    /// <summary>
    /// Decoded participant ids (service layer parses <c>ParticipantsJson</c>).
    /// </summary>
    public List<Guid> Participants { get; set; } = new();

    public int MessageCount { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// Chat session list item DTO (omits large text fields for paging efficiency).
/// </summary>
public class ChatSessionListItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public ChatSessionStatus Status { get; set; }
    public List<Guid> Participants { get; set; } = new();
    public int MessageCount { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// Shared fields for create/update requests.
/// </summary>
public abstract class ChatSessionRequestBase
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public ChatSessionStatus Status { get; set; } = ChatSessionStatus.Active;

    /// <summary>
    /// Optional participant user ids. Serialised to <c>ParticipantsJson</c>
    /// on the entity.
    /// </summary>
    public List<Guid> Participants { get; set; } = new();
}

public class CreateChatSessionDto : ChatSessionRequestBase { }

public class UpdateChatSessionDto : ChatSessionRequestBase { }

/// <summary>
/// Paged query for admin list view.
/// </summary>
public class ChatSessionQueryDto : PagedQueryDto
{
    protected override int DefaultPageSize => 20;

    /// <summary>
    /// Optional status filter (null = all).
    /// </summary>
    public ChatSessionStatus? Status { get; set; }

    /// <summary>
    /// Optional keyword (matches title and description, case-insensitive).
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// Optional participant filter — returns sessions whose
    /// <c>ParticipantsJson</c> contains this user id.
    /// </summary>
    public Guid? ParticipantId { get; set; }
}
