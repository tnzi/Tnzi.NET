namespace Tnzi.Storage.Workspace.Dtos;

/// <summary>
/// 回收站里的一项。
/// </summary>
public class RecycleBinEntryDto
{
    /// <summary>条目 id（还原 / 永久删除用它，不是文件或目录的 id）。</summary>
    public Guid Id { get; set; }

    /// <summary>被删的是文件还是目录。</summary>
    public RecycleBinItemType ItemType { get; set; }

    /// <summary>文件或目录的 id。</summary>
    public Guid ItemId { get; set; }

    /// <summary>删除时的名字。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>从哪里删的（删除时所在目录的路径，根目录为 <c>/</c>）。</summary>
    public string OriginalLocation { get; set; } = "/";

    /// <summary>占用的字节数（目录为其中全部文件之和）。在清除之前一直计入配额。</summary>
    public long Size { get; set; }

    /// <summary>包含的文件数（文件为 1）。</summary>
    public int FileCount { get; set; }

    /// <summary>文件的扩展名；目录为 null。</summary>
    public string? Extension { get; set; }

    /// <summary>文件的内容类型；目录为 null。</summary>
    public string? ContentType { get; set; }

    /// <summary>放进回收站的时间。</summary>
    public DateTime DeletedAt { get; set; }

    /// <summary>自动清除的时间；<c>Storage:RecycleBin:RetentionDays</c> 为 0 时为 null（不自动清除）。</summary>
    public DateTime? PurgeAt { get; set; }
}

/// <summary>
/// 把文件与目录放进回收站。
/// </summary>
public class MoveToRecycleBinRequest
{
    /// <summary>要删的文件。</summary>
    public List<Guid>? FileIds { get; set; }

    /// <summary>要删的目录（连同其中的一切）。</summary>
    public List<Guid>? FolderIds { get; set; }
}

/// <summary>
/// 按条目 id 对回收站做操作（还原 / 永久删除）。
/// </summary>
public class RecycleBinEntriesRequest
{
    /// <summary>回收站条目 id。</summary>
    [Required]
    public List<Guid> EntryIds { get; set; } = null!;
}

/// <summary>
/// 还原的结果。
/// </summary>
public class RecycleBinRestoreResult
{
    /// <summary>还原了几项。</summary>
    public int Restored { get; set; }

    /// <summary>原来所在的目录已经不在、因此回到了根目录的那几项（条目 id）。</summary>
    public List<Guid> RestoredToRoot { get; set; } = [];

    /// <summary>因为原位置已有同名目录而改了名的目录：条目 id → 新名字。</summary>
    public Dictionary<Guid, string> Renamed { get; set; } = [];
}

/// <summary>
/// 回收站列表的分页参数，最新删除的在前。
/// </summary>
public class RecycleBinQueryDto : PagedQueryDto
{
    /// <inheritdoc />
    protected override int DefaultPageSize => 50;

    /// <inheritdoc />
    protected override int MaxPageSize => 200;
}
