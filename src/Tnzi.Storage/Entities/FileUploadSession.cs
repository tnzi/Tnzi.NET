namespace Tnzi.Storage.Entities;

/// <summary>
/// 文件上传会话（用于分块上传）
/// </summary>
public class FileUploadSession : EntityBase<Guid>, IHasCreationTime, IHasCreator
{
    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件总大小（字节）
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// 分块大小（字节）
    /// </summary>
    public int ChunkSize { get; set; }

    /// <summary>
    /// 总分块数
    /// </summary>
    public int TotalChunks { get; set; }

    /// <summary>
    /// 已上传分块数
    /// </summary>
    public int UploadedChunks { get; set; }

    /// <summary>
    /// 已上传大小（字节）
    /// </summary>
    public long UploadedSize { get; set; }

    /// <summary>
    /// 文件MD5哈希值（可选，用于验证）
    /// </summary>
    public string? Md5Hash { get; set; }

    /// <summary>
    /// 是否已完成
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// 是否已取消
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime? CompletedTime { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 创建者ID
    /// </summary>
    public Guid? CreatorId { get; set; }

    /// <summary>
    /// 过期时间（超过此时间的会话将被清理）
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}
