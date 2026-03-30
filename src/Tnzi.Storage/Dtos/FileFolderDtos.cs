namespace Tnzi.Storage.Dtos;

/// <summary>
/// File folder DTO (output)
/// </summary>
public class FileFolderDto
{
    /// <summary>
    /// Folder ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Folder name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Parent folder ID
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Hierarchical path (e.g., "/root/sub1/sub2")
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Sort order
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Child folders (populated in tree queries)
    /// </summary>
    public List<FileFolderDto>? Children { get; set; }

    /// <summary>
    /// Number of files directly in this folder
    /// </summary>
    public int FileCount { get; set; }
}

/// <summary>
/// Create file folder request
/// </summary>
public class CreateFileFolderDto
{
    /// <summary>
    /// Folder name
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Parent folder ID (null for root folder)
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Optional description
    /// </summary>
    [MaxLength(1024)]
    public string? Description { get; set; }

    /// <summary>
    /// Sort order (default 0)
    /// </summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// Update file folder request
/// </summary>
public class UpdateFileFolderDto
{
    /// <summary>
    /// Folder name
    /// </summary>
    [MaxLength(256)]
    public string? Name { get; set; }

    /// <summary>
    /// Optional description
    /// </summary>
    [MaxLength(1024)]
    public string? Description { get; set; }

    /// <summary>
    /// Sort order
    /// </summary>
    public int? SortOrder { get; set; }
}

/// <summary>
/// Move files to folder request
/// </summary>
public class MoveFilesToFolderRequest
{
    /// <summary>
    /// File IDs to move
    /// </summary>
    [Required]
    public List<Guid> FileIds { get; set; } = null!;

    /// <summary>
    /// Target folder ID (null to move to root)
    /// </summary>
    public Guid? FolderId { get; set; }
}
