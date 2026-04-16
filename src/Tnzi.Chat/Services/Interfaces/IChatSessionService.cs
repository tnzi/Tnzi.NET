namespace Tnzi.Chat.Services;

/// <summary>
/// Admin-facing service for managing chat session groupings.
///
/// <para>
/// Tnzi.Chat models sessions as admin-curated labels rather than automatic
/// conversation aggregates; see <see cref="Entities.ChatSession"/> for the
/// rationale. Applications that want message-driven upsert behaviour can
/// subscribe to <c>MessageSentEvent</c> and call
/// <see cref="UpsertFromMessageAsync"/> with their own grouping rule.
/// </para>
/// </summary>
public interface IChatSessionService
{
    Task<Result<ChatSessionDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<IPagedList<ChatSessionListItemDto>>> GetPagedListAsync(ChatSessionQueryDto query, CancellationToken cancellationToken = default);

    Task<Result<ChatSessionDto>> CreateAsync(CreateChatSessionDto input, CancellationToken cancellationToken = default);

    Task<Result<ChatSessionDto>> UpdateAsync(Guid id, UpdateChatSessionDto input, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<int>> DeleteBatchAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert a session from a single message — increments
    /// <c>MessageCount</c>, refreshes <c>LastMessageAt</c>, merges the
    /// participant ids, and backfills title when empty. Creates a new
    /// session row if <paramref name="sessionId"/> is null or does not
    /// resolve to an existing row. Returns the upserted session id.
    /// </summary>
    /// <remarks>
    /// This method is the integration surface for applications that want
    /// automatic session aggregation; the framework itself does not call
    /// it from any built-in event handler.
    /// </remarks>
    Task<Result<Guid>> UpsertFromMessageAsync(
        Guid? sessionId,
        string title,
        IEnumerable<Guid> participantIds,
        DateTime messageTime,
        CancellationToken cancellationToken = default);
}
