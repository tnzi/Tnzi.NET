namespace Tnzi.AI.Dtos;

/// <summary>
/// A stored attachment as a message lists it (no content).
/// </summary>
public class AttachmentDto
{
    /// <summary>Attachment ID; its content is at <c>GET threads/{threadId}/attachments/{id}</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>Where it came from: <c>Upload</c> or <c>ToolResult</c>.</summary>
    public AttachmentOrigin Origin { get; set; }

    /// <summary>Media type, e.g. <c>image/png</c> or <c>application/pdf</c>.</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>File name, when one is known.</summary>
    public string? FileName { get; set; }

    /// <summary>Size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>For a tool image: the call that returned it.</summary>
    public string? ToolCallId { get; set; }
}

/// <summary>
/// The content of a stored attachment.
/// </summary>
public class AttachmentContentDto
{
    /// <summary>Media type.</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>File name, when one is known.</summary>
    public string? FileName { get; set; }

    /// <summary>The bytes.</summary>
    public byte[] Content { get; set; } = [];
}

/// <summary>
/// An attachment decoded from a chat request and checked against <c>AI:Chat:Attachments</c>.
/// </summary>
/// <param name="MediaType">Media type, taken from the bytes themselves.</param>
/// <param name="FileName">File name the client gave, if any.</param>
/// <param name="Content">The decoded bytes.</param>
public sealed record ChatAttachmentPayload(string MediaType, string? FileName, byte[] Content)
{
    /// <summary>Whether it is an image (otherwise a PDF document).</summary>
    public bool IsImage => MediaType.StartsWith("image/", StringComparison.Ordinal);
}

/// <summary>
/// One attachment to store (<c>IAgentAttachmentService.SaveAsync</c>).
/// </summary>
public sealed record NewAgentAttachment
{
    /// <summary>Where it came from.</summary>
    public required AttachmentOrigin Origin { get; init; }

    /// <summary>Media type.</summary>
    public required string MediaType { get; init; }

    /// <summary>The bytes.</summary>
    public required byte[] Content { get; init; }

    /// <summary>File name, if any.</summary>
    public string? FileName { get; init; }

    /// <summary>Thread it belongs to.</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>Message it is shown on.</summary>
    public Guid? MessageId { get; init; }

    /// <summary>External run it belongs to.</summary>
    public Guid? RunId { get; init; }

    /// <summary>For a tool image: the call that returned it.</summary>
    public string? ToolCallId { get; init; }

    /// <summary>User it belongs to.</summary>
    public Guid? OwnerId { get; init; }

    /// <summary>Tenant; null takes the ambient tenant.</summary>
    public Guid? TenantId { get; init; }
}
