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
    /// 原始文件名（模糊匹配）
    /// </summary>
    public string? OriginalName { get; set; }

    /// <summary>
    /// 排序字段，如 CreationTime, Size, OriginalName
    /// </summary>
    [MaxLength(64)]
    public string? SortBy { get; set; }

    /// <summary>
    /// 是否降序，false 表示升序
    /// </summary>
    public bool Descending { get; set; } = false;
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
/// 文件上传进度
/// </summary>
public class FileUploadProgress
{
    /// <summary>
    /// 上传会话ID
    /// </summary>
    public Guid UploadSessionId { get; set; }

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件总大小
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// 已上传大小
    /// </summary>
    public long UploadedSize { get; set; }

    /// <summary>
    /// 总分片数
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// 已上传分片数
    /// </summary>
    public int UploadedChunks { get; set; }

    /// <summary>
    /// 上传进度百分比 0-100
    /// </summary>
    public double ProgressPercentage => TotalSize > 0 ? (double)UploadedSize / TotalSize * 100 : 0;

    /// <summary>
    /// 是否已完成
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// 是否已取消
    /// </summary>
    public bool IsCancelled { get; set; }
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
/// 创建分享请求
/// </summary>
public class CreateShareRequest
{
    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// 最大访问次数
    /// </summary>
    public int? MaxAccessCount { get; set; }

    /// <summary>
    /// 分享密码
    /// </summary>
    [MaxLength(128)]
    public string? Password { get; set; }
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
/// 初始化分块上传请求
/// </summary>
public class InitiateChunkedUploadRequest
{
    /// <summary>
    /// 文件名
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件总大小（字节）
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// 分块大小（字节），默认 5MB
    /// </summary>
    public int ChunkSize { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// 整体 MD5（可选，用于校验）
    /// </summary>
    [MaxLength(64)]
    public string? Md5Hash { get; set; }
}

/// <summary>
/// 完成分块上传请求
/// </summary>
public class CompleteChunkedUploadRequest
{
    /// <summary>
    /// 是否临时文件
    /// </summary>
    public bool IsTemporary { get; set; } = false;
}