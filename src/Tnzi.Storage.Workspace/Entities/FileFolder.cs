namespace Tnzi.Storage.Workspace.Entities;

/// <summary>
/// Virtual directory entity for tree-based file organization
/// </summary>
public class FileFolder : FullAuditedEntity<Guid>, IMultiTenant, IHasOrder
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

    /// <summary>
    /// 这个目录随哪一条回收站条目进了回收站（它自己被删，或它的某个上级被删时一并带走）。
    /// 进回收站的目录同时是软删的（<see cref="FullAuditedEntity{TKey}.IsDeleted"/>），所有常规查询都看不见它；
    /// 这一列只用来在还原时找回<b>同一次</b>删除带走的那棵子树，与更早被单独删掉、或旧版本里直接删掉的目录分开。
    /// </summary>
    public Guid? RecycleBinEntryId { get; set; }

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
