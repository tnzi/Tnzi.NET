namespace Tnzi.Storage.Events;

/// <summary>
/// Event published after a file is successfully uploaded and saved
/// </summary>
public class FileUploadedEvent : EventBase
{
    /// <summary>
    /// File record ID
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// Original file name
    /// </summary>
    public string OriginalName { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Content type
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Storage provider name
    /// </summary>
    public string Provider { get; set; } = "Local";

    /// <summary>
    /// Whether the file is temporary
    /// </summary>
    public bool IsTemporary { get; set; }

    /// <summary>
    /// MD5 hash
    /// </summary>
    public string? Md5Hash { get; set; }

    /// <summary>
    /// Whether the file was reused via MD5 dedup
    /// </summary>
    public bool IsReused { get; set; }
}
