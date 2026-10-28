namespace Tnzi.AI.Rag.Entities;

/// <summary>
/// 文档块实体 - 存储文档块内容和向量嵌入
/// </summary>
public class DocumentChunk : EntityBase<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 所属知识库 ID
    /// </summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>
    /// 所属文档 ID
    /// </summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// 块文本内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 向量嵌入（pgvector 类型）
    /// </summary>
    public Vector Embedding { get; set; } = null!;

    /// <summary>
    /// 块在文档中的索引
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// 额外元数据（JSON 格式，存储来源页码等）
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// 父块 ID（预留）。当前没有任何切块策略或摄取路径写入它，恒为 null：层级切块接入摄取时只产出扁平的子块。
    /// </summary>
    public Guid? ParentChunkId { get; set; }
}
