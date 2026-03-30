namespace Tnzi.Storage.Events;

/// <summary>
/// Event published when a file folder is moved to a new parent
/// </summary>
public class FileFolderMovedEvent : EventBase
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
    /// Previous parent ID
    /// </summary>
    public Guid? OldParentId { get; set; }

    /// <summary>
    /// New parent ID
    /// </summary>
    public Guid? NewParentId { get; set; }

    /// <summary>
    /// Previous path
    /// </summary>
    public string OldPath { get; set; } = string.Empty;

    /// <summary>
    /// New path
    /// </summary>
    public string NewPath { get; set; } = string.Empty;
}
