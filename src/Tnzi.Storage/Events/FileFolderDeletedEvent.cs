namespace Tnzi.Storage.Events;

/// <summary>
/// Event published when a file folder is deleted
/// </summary>
public class FileFolderDeletedEvent : EventBase
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
    /// Full path
    /// </summary>
    public string Path { get; set; } = string.Empty;
}
