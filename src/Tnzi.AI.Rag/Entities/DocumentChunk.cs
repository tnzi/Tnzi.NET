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
    /// 父块 ID - 用于层级切块策略的父子关系（null 表示顶层块或非层级块）
    /// </summary>
    public Guid? ParentChunkId { get; set; }
}
