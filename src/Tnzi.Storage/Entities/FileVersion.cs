namespace Tnzi.Storage.Entities;

/// <summary>
/// 文件版本记录
/// </summary>
public class FileVersion : EntityBase<Guid>, IHasCreationTime, IHasCreator
{
    /// <summary>
    /// 文件ID（关联到 FileRecord）
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// 版本号（从1开始递增）
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// 文件路径（版本文件的实际存储路径）
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// MD5哈希
    /// </summary>
    public string? Md5Hash { get; set; }

    /// <summary>
    /// 版本描述（可选）
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 是否当前版本
    /// </summary>
    public bool IsCurrent { get; set; }

    // Audit info
    public DateTime CreationTime { get; set; }
    public Guid? CreatorId { get; set; }
}
