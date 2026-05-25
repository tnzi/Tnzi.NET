namespace Tnzi.Template.Dtos;

/// <summary>
/// 布局详情输出 DTO
/// </summary>
public class LayoutDto
{
    public Guid Id { get; set; }
    public string LayoutName { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string LayoutContent { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public string? Description { get; set; }
    public string? Metadata { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 布局请求基类（Create/Update共用字段）
/// </summary>
public abstract class LayoutRequestBase
{
    [Required]
    [MaxLength(200)]
    public string LayoutName { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string Module { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string Category { get; set; } = null!;

    [Required]
    public string LayoutContent { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public bool IsDefault { get; set; } = false;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(4000)]
    public string? Metadata { get; set; }
}

/// <summary>
/// 创建布局请求
/// </summary>
public class CreateLayoutRequest : LayoutRequestBase { }

/// <summary>
/// 更新布局请求
/// </summary>
public class UpdateLayoutRequest : LayoutRequestBase { }

/// <summary>
/// 查询布局请求
/// </summary>
public class QueryLayoutRequest : PagedQueryDto
{
    protected override int DefaultPageSize => 20;
    public string? Module { get; set; }
    public string? Category { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsDefault { get; set; }
    public string? Keyword { get; set; }

    /// <summary>Include filesystem-backed layouts in the paged result.</summary>
    public bool IncludeFileSource { get; set; }
}

/// <summary>
/// 布局信息DTO（分页列表项，不含大文本字段以优化传输性能）
/// </summary>
public class LayoutInfoDto
{
    public Guid Id { get; set; }
    public string LayoutName { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public string? Description { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }

    /// <summary>Origin of this layout row — "Database" or "FileSystem".</summary>
    public string Source { get; set; } = "Database";

    /// <summary>True for filesystem-fallback rows (shipped with binaries, not editable).</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>Absolute filesystem path of the source layout (file-source rows only).</summary>
    public string? FilePath { get; set; }
}

/// <summary>
/// Layout export entry (JSON-serializable format for import/export)
/// </summary>
public class LayoutExportEntry
{
    public string LayoutName { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string LayoutContent { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public string? Description { get; set; }
    public string? Metadata { get; set; }
}

/// <summary>
/// Layout import request
/// </summary>
public class LayoutImportRequest
{
    /// <summary>
    /// JSON string containing layouts to import
    /// </summary>
    [Required]
    public string Json { get; set; } = null!;

    /// <summary>
    /// Whether to overwrite existing layouts with same name+module+category
    /// </summary>
    public bool OverwriteExisting { get; set; } = false;
}

/// <summary>
/// Layout import result
/// </summary>
public class LayoutImportResultDto
{
    /// <summary>
    /// Number of layouts created
    /// </summary>
    public int CreatedCount { get; set; }

    /// <summary>
    /// Number of layouts updated (overwritten)
    /// </summary>
    public int UpdatedCount { get; set; }

    /// <summary>
    /// Number of layouts skipped (already exist)
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// Errors encountered during import
    /// </summary>
    public List<string> Errors { get; set; } = new();
}
