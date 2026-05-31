namespace Tnzi.AI.Rag.Entities;

/// <summary>
/// 知识文档实体 — 存储文档元数据
/// </summary>
public class KnowledgeDocument : AuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 所属知识库 ID
    /// </summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = null!;

    /// <summary>
    /// 文件 MIME 类型
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 切块数量
    /// </summary>
    public int ChunkCount { get; set; }

    /// <summary>
    /// 处理状态
    /// </summary>
    public DocumentStatus Status { get; set; } = DocumentStatus.Processing;

    /// <summary>
    /// 错误信息（处理失败时）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 文件内容 SHA256 哈希（用于去重检测）
    /// </summary>
    public string? ContentHash { get; set; }

    /// <summary>
    /// 文档版本号（保留字段，当前未使用，固定为默认值 1；预留给未来的内容版本管理）
    /// </summary>
    public int Version { get; set; } = 1;
}
