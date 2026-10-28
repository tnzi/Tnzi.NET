namespace Tnzi.AI.Entities;

/// <summary>
/// A file carried by a conversation: an image or document a user attached to a chat message, or an image a tool
/// returned during a run.
/// </summary>
/// <remarks>
/// <para>
/// The bytes are stored with the row, so a reloaded thread can show what was sent and what the tools returned, and an
/// external run can hand the user's attachments to its CLI on whichever host claims it. Size is bounded where rows are
/// written (<c>AI:Chat:Attachments</c> for uploads, the AI.Cli tool-result limits for tool images).
/// </para>
/// <para>
/// Who may read it is <see cref="OwnerId"/>: the author of the message for an upload, the user the run worked for
/// for a tool image. Reads also require the thread (when there is one) to belong to that user. Rows go with the
/// message they are shown on (rewinding a thread deletes them) and with the thread (deleting it deletes them).
/// </para>
/// </remarks>
public class AgentAttachment : CreationAuditedEntity<Guid>, IMultiTenant
{
    /// <inheritdoc />
    public Guid? TenantId { get; set; }

    /// <summary>Thread the attachment belongs to; null for a run without a thread.</summary>
    public Guid? ThreadId { get; set; }

    /// <summary>
    /// Thread message it is shown on: the user message for an upload, the assistant reply for a tool image
    /// (set when the reply is stored).
    /// </summary>
    public Guid? MessageId { get; set; }

    /// <summary>The external run that received it (upload) or produced it (tool image); null on the built-in path.</summary>
    public Guid? RunId { get; set; }

    /// <summary>For a tool image: the call that returned it.</summary>
    public string? ToolCallId { get; set; }

    /// <summary>User the content belongs to; the only caller allowed to read it.</summary>
    public Guid? OwnerId { get; set; }

    /// <summary>Where it came from.</summary>
    public AttachmentOrigin Origin { get; set; }

    /// <summary>Media type, e.g. <c>image/png</c> or <c>application/pdf</c>.</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>File name shown with it, when one is known.</summary>
    public string? FileName { get; set; }

    /// <summary>Size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>The bytes.</summary>
    public byte[] Content { get; set; } = [];
}
