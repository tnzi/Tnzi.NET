namespace Tnzi.Storage.Entities;

/// <summary>
/// Virtual directory entity for tree-based file organization
/// </summary>
public class FileFolder : FullAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// Tenant ID for multi-tenant isolation
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Folder name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Parent folder ID (null for root folders)
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Computed hierarchical path (e.g., "/root/sub1/sub2")
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Sort order within the same parent
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; set; }

    // Navigation properties

    /// <summary>
    /// Parent folder
    /// </summary>
    public FileFolder? Parent { get; set; }

    /// <summary>
    /// Child folders
    /// </summary>
    public ICollection<FileFolder> Children { get; set; } = new List<FileFolder>();
}
