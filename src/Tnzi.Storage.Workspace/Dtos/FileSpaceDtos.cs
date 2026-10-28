namespace Tnzi.Storage.Workspace.Dtos;

/// <summary>
/// 文件空间里的一项是目录还是文件。
/// </summary>
public enum FileSpaceEntryKind
{
    /// <summary>目录</summary>
    Folder = 0,

    /// <summary>文件</summary>
    File = 1
}

/// <summary>
/// 文件空间列表与搜索的排序字段。目录总是排在文件前面；按大小排时目录之间按名字排（目录没有大小）。
/// </summary>
public enum FileSpaceSortField
{
    /// <summary>名字（目录名 / 原始文件名）</summary>
    Name = 0,

    /// <summary>创建时间</summary>
    Time = 1,

    /// <summary>文件大小</summary>
    Size = 2
}

/// <summary>
/// 文件空间里的一项：一个目录，或一个文件。
/// </summary>
public class FileSpaceEntryDto
{
    /// <summary>目录还是文件。</summary>
    public FileSpaceEntryKind Kind { get; set; }

    /// <summary>目录 id 或文件 id（文件的 id 可直接用于 <c>files/{id}/download</c> / <c>preview</c> / <c>thumbnail</c>）。</summary>
    public Guid Id { get; set; }

    /// <summary>目录名，或文件的原始文件名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>所在目录；<c>null</c> = 根目录。</summary>
    public Guid? ParentId { get; set; }

    /// <summary>所在目录的路径（根目录为 <c>/</c>）。搜索结果靠它说清楚文件在哪。</summary>
    public string Location { get; set; } = "/";

    /// <summary>文件大小（字节）；目录为 0。</summary>
    public long Size { get; set; }

    /// <summary>扩展名（含点）；目录为 null。</summary>
    public string? Extension { get; set; }

    /// <summary>内容类型；目录为 null。</summary>
    public string? ContentType { get; set; }

    /// <summary>是否有缩略图（<c>files/{id}/thumbnail</c> 取得到）。</summary>
    public bool HasThumbnail { get; set; }

    /// <summary>是否公开可读。</summary>
    public bool IsPublic { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 面包屑上的一级目录。
/// </summary>
public class FileSpaceBreadcrumbDto
{
    /// <summary>目录 id。</summary>
    public Guid Id { get; set; }

    /// <summary>目录名。</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 一个目录（或根目录）的内容：先目录、后文件，一起分页。
/// </summary>
public class FileSpaceListingDto
{
    /// <summary>列出的目录；<c>null</c> = 根目录。</summary>
    public Guid? FolderId { get; set; }

    /// <summary>列出的目录的路径（根目录为 <c>/</c>）。</summary>
    public string Path { get; set; } = "/";

    /// <summary>从根目录往下到当前目录的每一级（不含根目录；当前目录是最后一项）。</summary>
    public List<FileSpaceBreadcrumbDto> Breadcrumbs { get; set; } = [];

    /// <summary>这一页的内容。目录在前、文件在后，分页跨过两者的分界。</summary>
    public IPagedList<FileSpaceEntryDto> Entries { get; set; } = null!;
}

/// <summary>
/// 列出一个目录的内容。
/// </summary>
public class FileSpaceListRequest : PagedQueryDto
{
    /// <summary>要列出的目录；不传 = 根目录。</summary>
    public Guid? FolderId { get; set; }

    /// <summary>排序字段，默认按名字。</summary>
    public FileSpaceSortField SortBy { get; set; } = FileSpaceSortField.Name;

    /// <summary>是否倒序。</summary>
    public bool Descending { get; set; }

    /// <inheritdoc />
    protected override int DefaultPageSize => 50;

    /// <inheritdoc />
    protected override int MaxPageSize => 200;
}

/// <summary>
/// 在自己的文件空间里按名字（与类型）找文件。
/// </summary>
public class FileSpaceSearchRequest : PagedQueryDto
{
    /// <summary>文件名里包含的文字（不区分大小写）。</summary>
    [MaxLength(256)]
    public string? Keyword { get; set; }

    /// <summary>只要这个扩展名（如 <c>.pdf</c>，不区分大小写；不带点也认）。</summary>
    [MaxLength(32)]
    public string? Extension { get; set; }

    /// <summary>只要内容类型以它开头的（如 <c>image/</c> 找全部图片、<c>application/pdf</c> 找 PDF）。</summary>
    [MaxLength(128)]
    public string? ContentType { get; set; }

    /// <summary>排序字段，默认按时间。</summary>
    public FileSpaceSortField SortBy { get; set; } = FileSpaceSortField.Time;

    /// <summary>是否倒序，默认最新在前。</summary>
    public bool Descending { get; set; } = true;

    /// <inheritdoc />
    protected override int DefaultPageSize => 50;

    /// <inheritdoc />
    protected override int MaxPageSize => 200;
}
