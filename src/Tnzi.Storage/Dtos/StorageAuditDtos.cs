namespace Tnzi.Storage.Dtos;

/// <summary>
/// Lightweight read DTO for a file chunk row returned by the admin audit
/// endpoint. Covers the fields the chunks-across-all-uploads view needs.
/// </summary>
public class FileChunkAuditDto
{
    public Guid Id { get; set; }
    public Guid UploadSessionId { get; set; }
    public int ChunkIndex { get; set; }
    public long ChunkSize { get; set; }
    public string? Md5Hash { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// Lightweight read DTO for a file version row returned by the admin audit
/// endpoint. Covers the fields the versions-across-all-files view needs.
/// </summary>
public class FileVersionAuditDto
{
    public Guid Id { get; set; }
    public Guid FileId { get; set; }
    public int Version { get; set; }
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? Md5Hash { get; set; }
    public string? Description { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
}

/// <summary>
/// Filter shape for the admin chunk audit list.
/// </summary>
public class FileChunkAuditQueryDto : PagedQueryDto
{
    /// <summary>
    /// Only return chunks for this upload session.
    /// </summary>
    public Guid? UploadSessionId { get; set; }
}

/// <summary>
/// Filter shape for the admin version audit list.
/// </summary>
public class FileVersionAuditQueryDto : PagedQueryDto
{
    /// <summary>
    /// Only return versions of this file.
    /// </summary>
    public Guid? FileId { get; set; }

    /// <summary>
    /// When true, return only the version marked as current per file.
    /// </summary>
    public bool? CurrentOnly { get; set; }
}
