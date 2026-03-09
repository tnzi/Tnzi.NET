namespace Tnzi.AI.Rag.Entities;

/// <summary>
/// 知识库实体
/// </summary>
public class KnowledgeBase : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 知识库名称
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// 知识库描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 嵌入提供商名称（对应 AI:Providers 配置）
    /// </summary>
    public string EmbeddingProvider { get; set; } = "default";

    /// <summary>
    /// 嵌入模型名称，null 使用默认模型
    /// </summary>
    public string? EmbeddingModel { get; set; }

    /// <summary>
    /// 切块大小（字符数）
    /// </summary>
    public int ChunkSize { get; set; } = 512;

    /// <summary>
    /// 切块重叠（字符数）
    /// </summary>
    public int ChunkOverlap { get; set; } = 128;

    /// <summary>
    /// 文档数量
    /// </summary>
    public int DocumentCount { get; set; }

    /// <summary>
    /// 总块数
    /// </summary>
    public int ChunkCount { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
