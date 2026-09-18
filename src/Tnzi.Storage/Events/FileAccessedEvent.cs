namespace Tnzi.Storage.Events;

/// <summary>
/// Event published when a file is downloaded or accessed
/// </summary>
public class FileAccessedEvent : EventBase
{
    /// <summary>
    /// File record ID
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// Access type
    /// </summary>
    public FileAccessType AccessType { get; set; }

    /// <summary>
    /// The historical version whose content was read (<c>GET files/{id}/versions/{version}/download</c>),
    /// or <c>null</c> when the file's current content was accessed.
    /// </summary>
    public int? Version { get; set; }
}

/// <summary>
/// File access types
/// </summary>
public enum FileAccessType
{
    /// <summary>
    /// Full download
    /// </summary>
    Download,

    /// <summary>
    /// Range/partial download
    /// </summary>
    RangeDownload,

    /// <summary>
    /// Thumbnail access
    /// </summary>
    Thumbnail,

    /// <summary>
    /// Preview
    /// </summary>
    Preview
}
