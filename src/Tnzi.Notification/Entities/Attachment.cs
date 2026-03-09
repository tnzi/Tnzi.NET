namespace Tnzi.Notification.Entities;

/// <summary>
/// 消息附件（仅Email）
/// </summary>
public class Attachment : EntityBase<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 消息ID
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// 消息实体
    /// </summary>
    public virtual Message Message { get; set; } = null!;

    /// <summary>
    /// 文件ID（来自FileStorage模块，用于文件引用跟踪）
    /// </summary>
    [FileField]
    public Guid? FileId { get; set; }

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 文件路径或URL
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// MIME类型
    /// </summary>
    public string ContentType { get; set; } = "application/octet-stream";
}

