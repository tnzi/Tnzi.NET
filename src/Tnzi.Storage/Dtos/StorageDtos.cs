namespace Tnzi.Storage.Dtos;

/// <summary>
/// 文件存储统计信息
/// </summary>
public class FileStorageStatistics
{
    /// <summary>
    /// 文件总数
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// 总大小（字节）
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// 按类型分组的文件统计
    /// </summary>
    public Dictionary<string, FileTypeStatistics> FilesByType { get; set; } = new();
}

/// <summary>
/// 文件类型统计信息
/// </summary>
public class FileTypeStatistics
{
    /// <summary>
    /// 数量
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// 大小（字节）
    /// </summary>
    public long Size { get; set; }
}

/// <summary>
/// 文件查询请求
/// </summary>
public class FileQueryRequest : PagedQueryDto
{
    /// <summary>
    /// 默认每页数量
    /// </summary>
    protected override int DefaultPageSize => 20;

    /// <summary>
    /// 文件扩展名，如 .jpg, .pdf
    /// </summary>
    [MaxLength(32)]
    public string? Extension { get; set; }

    /// <summary>
    /// 最小文件大小（字节）
    /// </summary>
    public long? MinSize { get; set; }

    /// <summary>
    /// 最大文件大小（字节）
    /// </summary>
    public long? MaxSize { get; set; }

    /// <summary>
    /// 创建时间起始
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 创建时间截止
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 创建者ID
    /// </summary>
    public Guid? CreatorId { get; set; }

    /// <summary>
    /// 存储提供商，如 Local, S3, Azure
    /// </summary>
    [MaxLength(50)]
    public string? Provider { get; set; }

    /// <summary>
    /// MIME content type prefix filter (e.g. "image/" matches all images, "application/pdf" matches PDFs).
    /// Matched via StartsWith so a trailing-slash prefix selects a whole category.
    /// </summary>
    [MaxLength(128)]
    public string? ContentType { get; set; }

    /// <summary>
    /// 文件夹 ID 过滤。传入 Guid.Empty 表示"根目录（未归档）"，
    /// 传入具体 ID 表示该文件夹直接子文件，不传则不按文件夹过滤。
    /// </summary>
    public Guid? FolderId { get; set; }

    /// <summary>
    /// 当 FolderId 传入 Guid.Empty 时，表示要查根目录下未归档的文件。
    /// 配合 IncludeUnfiled 一起使用以区分"明确指定根目录" vs "不过滤"。
    /// </summary>
    public bool IncludeUnfiled { get; set; }

    /// <summary>
    /// 原始文件名（模糊匹配）
    /// </summary>
    public string? OriginalName { get; set; }

    /// <summary>
    /// Filter by tag (exact match)
    /// </summary>
    [MaxLength(128)]
    public string? Tag { get; set; }

    /// <summary>
    /// Filter by metadata key (requires MetadataValue to also be set for exact match)
    /// </summary>
    [MaxLength(128)]
    public string? MetadataKey { get; set; }

    /// <summary>
    /// Filter by metadata value (used with MetadataKey for exact key-value match)
    /// </summary>
    [MaxLength(256)]
    public string? MetadataValue { get; set; }

    /// <summary>
    /// 排序字段，如 CreationTime, Size, OriginalName
    /// </summary>
    [MaxLength(64)]
    public string? SortBy { get; set; }

    /// <summary>
    /// 是否降序，false 表示升序
    /// </summary>
    public bool Descending { get; set; } = false;

    /// <summary>
    /// Sort direction as a string ("asc"/"desc"). When set, takes precedence over
    /// <see cref="Descending"/> so frontends sending a string direction work correctly.
    /// Leave null to fall back to <see cref="Descending"/>.
    /// </summary>
    [MaxLength(8)]
    public string? SortOrder { get; set; }
}

/// <summary>
/// 文件引用 DTO
/// </summary>
public class FileReferenceDto
{
    /// <summary>
    /// 引用ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 文件ID
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// 实体类型
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// 实体ID
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// 字段名
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// 是否为临时引用
    /// </summary>
    public bool IsTemporary { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 文件引用统计信息
/// </summary>
public class FileReferenceStatistics
{
    /// <summary>
    /// 引用总数
    /// </summary>
    public int TotalReferences { get; set; }

    /// <summary>
    /// 永久引用数
    /// </summary>
    public int PermanentReferences { get; set; }

    /// <summary>
    /// 临时引用数
    /// </summary>
    public int TemporaryReferences { get; set; }

    /// <summary>
    /// 按实体类型分组的引用数
    /// </summary>
    public Dictionary<string, int> ReferencesByEntityType { get; set; } = new();
}

/// <summary>
/// 文件引用信息（精简版）
/// </summary>
public class FileReferenceInfo
{
    /// <summary>
    /// 文件ID
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// 实体类型
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// 实体ID
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// 字段名
    /// </summary>
    public string FieldName { get; set; } = string.Empty;
}

/// <summary>
/// 重命名文件请求
/// </summary>
public class RenameFileRequest
{
    /// <summary>
    /// 新文件名
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string NewFileName { get; set; } = string.Empty;
}

/// <summary>
/// 复制文件请求
/// </summary>
public class CopyFileRequest
{
    /// <summary>
    /// 可选的新文件名（不传则自动生成）
    /// </summary>
    public string? NewFileName { get; set; }
}

/// <summary>
/// 压缩请求
/// </summary>
public class CompressRequest
{
    /// <summary>
    /// 待压缩文件 ID 列表
    /// </summary>
    public IEnumerable<Guid> FileIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// ZIP 文件名（可选）
    /// </summary>
    [MaxLength(256)]
    public string? ZipFileName { get; set; }
}

/// <summary>
/// User storage usage statistics
/// </summary>
public class UserStorageUsage
{
    /// <summary>
    /// User ID (null for anonymous uploads)
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Total number of files uploaded by the user
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// Total storage size in bytes
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// Formatted total size (e.g., "12.5 MB")
    /// </summary>
    public string FormattedSize => FormatFileSize(TotalSize);

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}

/// <summary>
/// File integrity verification result
/// </summary>
public class FileIntegrityResult
{
    /// <summary>
    /// File ID
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// Original file name
    /// </summary>
    public string OriginalName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the physical file exists on storage
    /// </summary>
    public bool PhysicalFileExists { get; set; }

    /// <summary>
    /// Whether the MD5 hash matches (null if file doesn't exist or MD5 not stored)
    /// </summary>
    public bool? Md5Matches { get; set; }

    /// <summary>
    /// Expected MD5 hash (from database)
    /// </summary>
    public string? ExpectedMd5 { get; set; }

    /// <summary>
    /// Actual MD5 hash (from physical file, null if file doesn't exist)
    /// </summary>
    public string? ActualMd5 { get; set; }

    /// <summary>
    /// Overall integrity status
    /// </summary>
    public FileIntegrityStatus Status { get; set; }

    /// <summary>
    /// Error message (if any)
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// File integrity status
/// </summary>
public enum FileIntegrityStatus
{
    /// <summary>
    /// File is healthy - exists and MD5 matches
    /// </summary>
    Healthy,

    /// <summary>
    /// Physical file is missing from storage
    /// </summary>
    Missing,

    /// <summary>
    /// File exists but MD5 hash does not match (corrupted)
    /// </summary>
    Corrupted,

    /// <summary>
    /// Unable to verify (error during check)
    /// </summary>
    Error
}

/// <summary>
/// Batch integrity verification result
/// </summary>
public class BatchIntegrityResult
{
    /// <summary>
    /// Total files checked
    /// </summary>
    public int TotalChecked { get; set; }

    /// <summary>
    /// Number of healthy files
    /// </summary>
    public int Healthy { get; set; }

    /// <summary>
    /// Number of missing files
    /// </summary>
    public int Missing { get; set; }

    /// <summary>
    /// Number of corrupted files
    /// </summary>
    public int Corrupted { get; set; }

    /// <summary>
    /// Number of files with errors during verification
    /// </summary>
    public int Errors { get; set; }

    /// <summary>
    /// Details of problematic files (missing/corrupted/error only)
    /// </summary>
    public List<FileIntegrityResult> Problems { get; set; } = new();
}

/// <summary>
/// 缩略图回填请求（<c>POST admin/files/backfill-thumbnails</c>）。
/// </summary>
public class ThumbnailBackfillRequest
{
    /// <summary>
    /// 只处理这些文件；为空或不给表示扫描全部没有缩略图的记录。
    /// </summary>
    public List<Guid>? FileIds { get; set; }

    /// <summary>
    /// 一次最多处理多少条，默认 100；0 表示不限（几百份 PDF 会串行渲染几十秒，慎用）。
    /// </summary>
    public int MaxFiles { get; set; } = 100;
}

/// <summary>
/// 缩略图回填结果。
/// </summary>
/// <remarks>
/// 画不出来的记录（有口令、损坏、超限）仍然没有缩略图，下一次调用还会再试；所以循环调用时按
/// <see cref="Generated"/> 归零停手，而不是等 <see cref="Remaining"/> 归零 —— 后者在存在画不出来的文件时永远不会归零。
/// </remarks>
public class ThumbnailBackfillResult
{
    /// <summary>
    /// 本批检查的记录数（没有缩略图、且此刻画得出来的那些，按 MaxFiles 截断）。
    /// </summary>
    public int Scanned { get; set; }

    /// <summary>
    /// 成功画出并写回的记录数。
    /// </summary>
    public int Generated { get; set; }

    /// <summary>
    /// 画不出来的记录数（原因在服务端日志里）。
    /// </summary>
    public int Failed { get; set; }

    /// <summary>
    /// 本批之后仍没有缩略图的候选记录数（含本批画不出来的）。
    /// </summary>
    public int Remaining { get; set; }

    /// <summary>
    /// 本批画不出来的文件 id。
    /// </summary>
    public List<Guid> FailedFileIds { get; set; } = [];
}

/// <summary>
/// Set file tags request
/// </summary>
public class SetFileTagsRequest
{
    /// <summary>
    /// Tags to set (replaces existing tags)
    /// </summary>
    public List<string> Tags { get; set; } = null!;
}

/// <summary>
/// Set file metadata request
/// </summary>
public class SetFileMetadataRequest
{
    /// <summary>
    /// Metadata key-value pairs to set (replaces existing metadata)
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = null!;
}

/// <summary>
/// Set file visibility request
/// </summary>
public class SetFileVisibilityRequest
{
    /// <summary>
    /// True makes the file readable by anyone (including unauthenticated callers);
    /// false restores the default "owner or storage.file.view" policy.
    /// </summary>
    public bool IsPublic { get; set; }
}

/// <summary>
/// A short-lived token that lets a browser fetch one private file without an
/// Authorization header. Append it to any read URL of that file as the `sig`
/// query parameter (download / preview / thumbnail all accept it).
/// </summary>
public class FileAccessTokenDto
{
    /// <summary>File the token is valid for. A token never works for another file.</summary>
    public Guid FileId { get; set; }

    /// <summary>Opaque signed token. Pass it as the `sig` query parameter.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Expiry instant (UTC). Refresh before this to keep long-lived pages working.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Batch request for file access tokens.
/// </summary>
/// <remarks>
/// A list page renders many files at once; minting one token per request would
/// turn a single screen into N round trips. Files the caller may not read are
/// omitted from the response rather than failing the batch, so one unreadable
/// id never blanks the whole page (and the omission does not reveal whether
/// that id exists).
/// </remarks>
public class FileAccessTokenRequest
{
    /// <summary>File ids to mint tokens for.</summary>
    public List<Guid> FileIds { get; set; } = null!;

    /// <summary>Optional lifetime override in seconds. Defaults to `Storage:SignedUrlTtlSeconds`.</summary>
    public int? ExpiresInSeconds { get; set; }
}