namespace Tnzi.Storage.Events;

/// <summary>
/// Event published when a file folder is created
/// </summary>
public class FileFolderCreatedEvent : EventBase
{
    /// <summary>
    /// Folder ID
    /// </summary>
    public Guid FolderId { get; set; }

    /// <summary>
    /// Folder name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Parent folder ID
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Full path
    /// </summary>
    public string Path { get; set; } = string.Empty;
}
