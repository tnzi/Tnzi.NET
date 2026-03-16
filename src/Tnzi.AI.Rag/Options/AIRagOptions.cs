namespace Tnzi.AI.Rag.Options;

/// <summary>
/// AI RAG 模块配置选项
/// </summary>
public class AIRagOptions
{
    /// <summary>
    /// 默认切块大小（字符数）
    /// </summary>
    public int DefaultChunkSize { get; set; } = 1024;

    /// <summary>
    /// 默认切块重叠（字符数）
    /// </summary>
    public int DefaultChunkOverlap { get; set; } = 128;

    /// <summary>
    /// 默认嵌入维度
    /// </summary>
    public int DefaultEmbeddingDimensions { get; set; } = 1536;

    /// <summary>
    /// 嵌入生成的批量大小
    /// </summary>
    public int EmbeddingBatchSize { get; set; } = 200;

    /// <summary>
    /// 默认嵌入提供商名称（对应 AI:Providers 中的配置）
    /// </summary>
    public string? DefaultEmbeddingProvider { get; set; }

    /// <summary>
    /// 默认嵌入模型名称
    /// </summary>
    public string? DefaultEmbeddingModel { get; set; }

    /// <summary>
    /// 向量存储提供者 (Auto/InMemory/PostgreSQL)
    /// </summary>
    public string VectorStoreProvider { get; set; } = "Auto";

    /// <summary>
    /// 查询改写使用的 AI 提供商名称（对应 AI:Providers 中的配置，null 使用默认提供商）
    /// </summary>
    public string? QueryRewriteProvider { get; set; }

    /// <summary>
    /// 查询改写使用的 AI 模型名称（null 使用提供商默认模型）
    /// </summary>
    public string? QueryRewriteModel { get; set; }

    /// <summary>
    /// 相关性评分使用的 AI 提供商名称（对应 AI:Providers 中的配置，null 使用默认提供商）
    /// </summary>
    public string? RelevanceGradeProvider { get; set; }

    /// <summary>
    /// 相关性评分使用的 AI 模型名称（null 使用提供商默认模型）
    /// </summary>
    public string? RelevanceGradeModel { get; set; }

    /// <summary>
    /// 搜索 TopK 最大值限制
    /// </summary>
    public int MaxTopK { get; set; } = 100;

    /// <summary>
    /// 数据库表名前缀（必须与 RagModule.TableNamePrefix 一致）
    /// </summary>
    public string TableNamePrefix { get; set; } = "RAG";

    /// <summary>
    /// 混合搜索配置
    /// </summary>
    public HybridSearchOptions HybridSearch { get; set; } = new();

    /// <summary>
    /// Embedding 缓存配置
    /// </summary>
    public EmbeddingCacheOptions EmbeddingCache { get; set; } = new();
}

/// <summary>
/// Embedding 缓存配置选项
/// </summary>
public class EmbeddingCacheOptions
{
    /// <summary>
    /// 是否启用 Embedding 缓存
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 缓存 TTL（小时），默认 720 小时（30 天）
    /// </summary>
    public int TtlHours { get; set; } = 720;
}
