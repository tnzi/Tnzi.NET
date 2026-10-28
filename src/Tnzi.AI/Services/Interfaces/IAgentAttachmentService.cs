namespace Tnzi.AI.Services;

/// <summary>
/// Stored attachments of conversations: files users attached to chat messages and images tools returned.
/// </summary>
public interface IAgentAttachmentService
{
    /// <summary>
    /// Read an attachment for its owner. Answers 404 when it does not exist, does not belong to
    /// <paramref name="threadId"/>, or is not <paramref name="userId"/>'s (the three are not told apart).
    /// </summary>
    Task<Result<AttachmentContentDto>> GetContentAsync(
        Guid threadId, Guid attachmentId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Store attachments and return their ids in the same order.</summary>
    Task<IReadOnlyList<Guid>> SaveAsync(
        IReadOnlyList<NewAgentAttachment> attachments, CancellationToken cancellationToken = default);

    /// <summary>
    /// The attachments of one origin that belong to an external run, oldest first, with their content
    /// (the uploads an external run hands to its CLI). Not filtered by tenant: the run id is the scope.
    /// </summary>
    Task<IReadOnlyList<AgentAttachment>> GetForRunAsync(
        Guid runId, AttachmentOrigin origin, CancellationToken cancellationToken = default);

    /// <summary>The attachments shown on a message, oldest first, with their content.</summary>
    Task<IReadOnlyList<AgentAttachment>> GetForMessageAsync(
        Guid messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Show the tool images an external run stored on the reply it wrote. Not filtered by tenant: the run id is the scope.
    /// </summary>
    Task BindToMessageAsync(Guid runId, AttachmentOrigin origin, Guid messageId, CancellationToken cancellationToken = default);
}
