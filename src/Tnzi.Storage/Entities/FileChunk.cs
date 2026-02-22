namespace Tnzi.Storage.Entities;

/// <summary>
/// 文件分块（用于分块上传）
/// </summary>
public class FileChunk : EntityBase<Guid>, IHasCreationTime
{
    /// <summary>
    /// 上传会话ID
    /// </summary>
    public Guid UploadSessionId { get; set; }

    /// <summary>
    /// 分块索引（从0开始）
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// 分块大小（字节）
    /// </summary>
    public long ChunkSize { get; set; }

    /// <summary>
    /// 分块临时存储路径
    /// </summary>
    public string? ChunkPath { get; set; }

    /// <summary>
    /// 分块MD5哈希值（用于验证）
    /// </summary>
    public string? Md5Hash { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}
