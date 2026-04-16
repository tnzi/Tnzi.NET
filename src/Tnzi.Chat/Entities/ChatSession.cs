namespace Tnzi.Chat.Entities;

/// <summary>
/// ChatSession — a logical grouping of related chat messages (a thread/topic).
///
/// <para>
/// Tnzi.Chat is an in-app announcement / directed-message module rather than
/// a realtime IM system, so sessions are <b>admin-curated groupings</b> rather
/// than auto-created conversations. An admin creates a session with a title,
/// description, optional participant hint list, and lifecycle status; users
/// (or other services) can then reference the session id on individual
/// messages via metadata.
/// </para>
///
/// <para>
/// The framework intentionally does not auto-upsert sessions from
/// <see cref="Events.MessageSentEvent"/> — the broadcast semantics of Chat
/// (role-based + private) do not map cleanly onto a single "conversation"
/// primitive, and any auto-grouping heuristic would need domain-specific
/// tuning. Applications that want automatic session upsert should subscribe
/// to <c>MessageSentEvent</c> themselves and call
/// <see cref="Services.IChatSessionService.UpsertFromMessageAsync"/>.
/// </para>
/// </summary>
public class ChatSession : FullAuditedEntity<Guid>
{
    /// <summary>
    /// Human-readable session title (shown in admin lists and user inboxes).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Optional longer description / purpose summary.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Lifecycle status (Active / Archived).
    /// </summary>
    public ChatSessionStatus Status { get; set; } = ChatSessionStatus.Active;

    /// <summary>
    /// JSON-encoded array of participant user ids. Denormalised so the
    /// admin list page can render "participants" without N+1 joins.
    /// Format: <c>["guid1","guid2",...]</c>; may be empty.
    /// </summary>
    public string ParticipantsJson { get; set; } = "[]";

    /// <summary>
    /// Cached count of messages logically belonging to this session.
    /// Maintained by <see cref="Services.IChatSessionService"/> on upsert
    /// and by explicit admin actions; not authoritative — a periodic
    /// reconcile job can rebuild it from messages if the app wires the
    /// upsert hook manually.
    /// </summary>
    public int MessageCount { get; set; }

    /// <summary>
    /// Timestamp of the most recent message logically belonging to this
    /// session. Null for freshly-created empty sessions.
    /// </summary>
    public DateTime? LastMessageAt { get; set; }
}
