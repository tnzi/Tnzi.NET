namespace Tnzi.AI.Rag.Options;

/// <summary>
/// AI RAG 模块配置选项
/// </summary>
[ConfigSection("AI:Rag")]
[RuntimeSettingGroup(Key = "ai-rag", Module = "AI", DisplayName = "RAG",
    I18nKey = "admin.modules.system.settings.groups.aiRag", Icon = "mdi:book-search-outline", Order = 155)]
public class AIRagOptions
{
    /// <summary>
    /// 默认切块大小（字符数）
    /// </summary>
    [RuntimeSetting(Label = "Default Chunk Size", I18n = "admin.modules.system.settings.fields.ragDefaultChunkSize",
        Type = SettingFieldType.Int, Min = 128, Max = 8192, Subsection = "Chunking & Retrieval",
        Description = "Default number of characters per chunk when a knowledge base does not override it")]
    public int DefaultChunkSize { get; set; } = 1024;

    /// <summary>
    /// 默认切块重叠（字符数）
    /// </summary>
    [RuntimeSetting(Label = "Default Chunk Overlap", I18n = "admin.modules.system.settings.fields.ragDefaultChunkOverlap",
        Type = SettingFieldType.Int, Min = 0, Subsection = "Chunking & Retrieval",
        Description = "Default overlap in characters between consecutive chunks (must be less than chunk size)")]
    public int DefaultChunkOverlap { get; set; } = 128;

    /// <summary>
    /// 默认嵌入维度
    /// </summary>
    public int DefaultEmbeddingDimensions { get; set; } = 1536;

    /// <summary>
    /// 嵌入生成的批量大小
    /// </summary>
    [RuntimeSetting(Label = "Embedding Batch Size", I18n = "admin.modules.system.settings.fields.ragEmbeddingBatchSize",
        Type = SettingFieldType.Int, Min = 1, Max = 1000, Subsection = "Chunking & Retrieval",
        Description = "Number of texts embedded per batch call during ingestion")]
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
    [RuntimeSetting(Label = "Max Top-K", I18n = "admin.modules.system.settings.fields.ragMaxTopK",
        Type = SettingFieldType.Int, Min = 1, Max = 1000, Subsection = "Chunking & Retrieval",
        Description = "Upper bound the search TopK is clamped to")]
    public int MaxTopK { get; set; } = 100;

    /// <summary>
    /// 数据库表名前缀（必须与 AIRagModule.TableNamePrefix 一致）
    /// </summary>
    public string TableNamePrefix { get; set; } = "RAG";

    /// <summary>
    /// RagDbContext 迁移文件所在的程序集名称（如 "Acme.Api"）。
    /// <para>
    /// RagDbContext 定义在框架库 Tnzi.AI.Rag 中，但其迁移文件应由消费应用持有
    /// （因为嵌入维度 <see cref="DefaultEmbeddingDimensions"/> 可配置，不能锁死在框架库的迁移里）。
    /// 运行时通过此项让 RagDbContext 在执行迁移时查找正确程序集中的迁移；
    /// 设计时由 <c>RagDbContext.DesignTimeMigrationsAssembly</c> 静态字段提供（参见设计时工厂）。
    /// </para>
    /// <para>未配置时回退到 RagDbContext 所在程序集（Tnzi.AI.Rag）。</para>
    /// </summary>
    public string? MigrationsAssembly { get; set; }

    /// <summary>
    /// 混合搜索配置
    /// </summary>
    public HybridSearchOptions HybridSearch { get; set; } = new();

    /// <summary>
    /// Embedding 缓存配置
    /// </summary>
    public EmbeddingCacheOptions EmbeddingCache { get; set; } = new();

    /// <summary>
    /// 加权递减重排序器配置
    /// </summary>
    public RerankerOptions Reranker { get; set; } = new();

    /// <summary>
    /// Parent Document Retrieval 配置
    /// </summary>
    public ParentDocumentRetrievalOptions ParentDocumentRetrieval { get; set; } = new();

    /// <summary>
    /// GraphRAG（知识图谱）配置
    /// </summary>
    public GraphRagOptions GraphRag { get; set; } = new();
}

/// <summary>
/// GraphRAG（知识图谱）配置选项
/// </summary>
[ConfigSection("AI:Rag:GraphRag")]
[RuntimeSettingGroup(Key = "ai-rag", Module = "AI", DisplayName = "RAG",
    I18nKey = "admin.modules.system.settings.groups.aiRag", Icon = "mdi:book-search-outline", Order = 155)]
public class GraphRagOptions
{
    /// <summary>
    /// 是否在文档摄取时提取知识图谱实体/关系（默认 false）。
    /// <para>
    /// 默认关闭：开启后每次摄取会额外调用一次 LLM（<see cref="Graph.IGraphExtractor"/>）进行实体抽取，
    /// 带来额外的延迟和成本。关闭时图谱表保持为空，<see cref="Graph.IGraphSearchService"/> 返回空结果，
    /// 摄取行为与成本不变。
    /// </para>
    /// <para>
    /// 运行时分支（非装配门）：<c>IGraphExtractor</c> 始终注册，本开关仅在
    /// <see cref="Services.DocumentIngestionService"/> 摄取时决定是否执行图谱抽取，因此可热更新。
    /// </para>
    /// </summary>
    [RuntimeSetting(Label = "GraphRAG Enabled", I18n = "admin.modules.system.settings.fields.ragGraphRagEnabled",
        Type = SettingFieldType.Boolean, Subsection = "GraphRAG",
        Description = "Extract knowledge-graph entities/relations during ingestion (adds an extra LLM call per document)")]
    public bool Enabled { get; set; }
}

/// <summary>
/// Parent Document Retrieval 配置选项
/// </summary>
[ConfigSection("AI:Rag:ParentDocumentRetrieval")]
[RuntimeSettingGroup(Key = "ai-rag", Module = "AI", DisplayName = "RAG",
    I18nKey = "admin.modules.system.settings.groups.aiRag", Icon = "mdi:book-search-outline", Order = 155)]
public class ParentDocumentRetrievalOptions
{
    /// <summary>
    /// 是否启用 Parent Document Retrieval（默认 false）
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 窗口大小 — 匹配块前后各扩展的块数（默认 2）
    /// </summary>
    [RuntimeSetting(Label = "Parent Window Size", I18n = "admin.modules.system.settings.fields.ragParentWindowSize",
        Type = SettingFieldType.Int, Min = 1, Max = 10, Subsection = "Parent Retrieval",
        Description = "Number of chunks expanded before and after each matched chunk")]
    public int WindowSize { get; set; } = 2;

    /// <summary>
    /// 每个合并结果的最大 Token 数（默认 4000）
    /// </summary>
    [RuntimeSetting(Label = "Parent Max Tokens", I18n = "admin.modules.system.settings.fields.ragParentMaxTokens",
        Type = SettingFieldType.Int, Min = 256, Max = 32000, Subsection = "Parent Retrieval",
        Description = "Maximum token count per merged parent-document result")]
    public int MaxTokens { get; set; } = 4000;
}

/// <summary>
/// Embedding 缓存配置选项
/// </summary>
[ConfigSection("AI:Rag:EmbeddingCache")]
[RuntimeSettingGroup(Key = "ai-rag", Module = "AI", DisplayName = "RAG",
    I18nKey = "admin.modules.system.settings.groups.aiRag", Icon = "mdi:book-search-outline", Order = 155)]
public class EmbeddingCacheOptions
{
    /// <summary>
    /// 是否启用 Embedding 缓存
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 缓存 TTL（小时），默认 720 小时（30 天）
    /// </summary>
    [RuntimeSetting(Label = "Embedding Cache TTL (hours)", I18n = "admin.modules.system.settings.fields.ragEmbeddingCacheTtlHours",
        Type = SettingFieldType.Int, Min = 1, Max = 8760, Subsection = "Embedding Cache",
        Description = "Time-to-live in hours for cached embeddings")]
    public int TtlHours { get; set; } = 720;
}
