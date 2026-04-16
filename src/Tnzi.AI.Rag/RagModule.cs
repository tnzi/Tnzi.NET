namespace Tnzi.AI.Rag;

/// <summary>
/// RAG (Retrieval-Augmented Generation) module providing document ingestion, chunking, and vector search.
/// <para>
/// Supports multiple vector store backends via <c>AI:Rag:VectorStoreProvider</c> configuration:
/// <list type="bullet">
/// <item><c>Auto</c> (default) — Uses PgVectorStore, can be overridden by pre-registering a custom IVectorStore</item>
/// <item><c>PostgreSQL</c> — PgVectorStore (requires PostgreSQL with pgvector extension)</item>
/// <item><c>InMemory</c> — InMemoryVectorStore (development/testing only, data not persisted)</item>
/// </list>
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// 依赖 AIModule，在其之后加载。AIModule 用 TryAddScoped 注册 NoOpTextSearchService，
/// 本模块用 RemoveAll + AddScoped 替换为真实搜索实现。
/// </para>
/// </remarks>
[DependsOn(typeof(AI.AIModule))]
public class RagModule : TnziApplicationModule
{
    /// <summary>
    /// 在 AIModule(50)、AICoderModule(51) 之后加载
    /// </summary>
    public override int LoadOrder => 55;

    /// <summary>
    /// 数据库表名前缀
    /// </summary>
    public override string? TableNamePrefix => "RAG";

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 绑定 AI:Rag 配置节
        context.Services.AddOptions<AIRagOptions>()
            .Bind(context.Configuration.GetSection("AI:Rag"))
            .ValidateWith<AIRagOptions, AIRagOptionsValidator>();

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 向量存储（根据配置选择后端实现）
        var vectorStoreProvider = context.Configuration
            .GetSection("AI:Rag")
            .GetValue<string>("VectorStoreProvider") ?? "Auto";

        switch (vectorStoreProvider)
        {
            case "InMemory":
                services.AddSingleton<IVectorStore, InMemoryVectorStore>();
                break;
            case "PostgreSQL":
                services.AddSingleton<IVectorStore, PgVectorStore>();
                break;
            default: // "Auto" — 使用 TryAdd，允许用户提前注册自定义实现
                services.TryAddSingleton<IVectorStore, PgVectorStore>();
                break;
        }

        // 文件提取器
        services.AddSingleton<IFileExtractorService, PdfFileExtractor>();
        services.AddSingleton<IFileExtractorService, WordFileExtractor>();
        services.AddSingleton<IFileExtractorService, MarkdownFileExtractor>();
        services.AddSingleton<IFileExtractorService, PlainTextFileExtractor>();
        services.AddSingleton<IFileExtractorService, TableContentExtractor>();
        services.AddSingleton<IFileExtractorService, ImageContentExtractor>();

        // 切块策略（默认使用固定大小）
        services.TryAddSingleton<IChunkingStrategy, FixedSizeChunkingStrategy>();

        // 重排序器（TryAdd: 用户可替换为商业 reranker，如 Cohere/Jina/bge-reranker）
        services.TryAddScoped<IReranker, NoOpReranker>();

        // 查询改写器（TryAdd: 用户可替换为 LlmQueryRewriter 或自定义实现）
        services.TryAddScoped<IQueryRewriter, NoOpQueryRewriter>();

        // 相关性评分器（TryAdd: 用户可替换为 LlmRelevanceGrader 或自定义实现）
        services.TryAddScoped<IRelevanceGrader, NoOpRelevanceGrader>();

        // Search post-processors (run in Order sequence after vector search)
        // Users can register additional ISearchPostProcessor implementations
        services.AddScoped<ISearchPostProcessor, DeduplicationPostProcessor>();
        services.AddScoped<ISearchPostProcessor, ScoreNormalizationPostProcessor>();
        services.AddScoped<ISearchPostProcessor, WeightedDiminishingReranker>();

        // 知识图谱提取 + 搜索
        services.AddScoped<IGraphExtractor, LlmGraphExtractor>();
        services.AddScoped<IGraphSearchService, GraphSearchService>();

        // 核心业务服务
        services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
        services.AddScoped<IIncrementalIndexService, IncrementalIndexService>();
        services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();

        // Parent Document Retriever（可选，通过 AI:Rag:ParentDocumentRetrieval:Enabled 启用）
        services.AddScoped<IParentDocumentRetriever, ParentDocumentRetriever>();

        // RAG 检索器 + 查询引擎 + 聊天引擎（Query/Chat split）
        services.AddScoped<IRagRetriever, RagRetriever>();
        services.AddScoped<IRagQueryEngine, RagQueryEngine>();
        services.AddScoped<IRagChatEngine, RagChatEngine>();

        // 后台摄取任务（需配合 Hangfire 模块使用，无 Hangfire 时 KnowledgeBaseService 自动回退同步模式）
        services.AddScoped<IBackgroundJob<DocumentIngestionJobArgs>, DocumentIngestionBackgroundJob>();

        // Embedding 缓存装饰器（条件启用）
        // 使用 keyed services 实现装饰器模式，避免 new 手动构造带来的耦合
        var embeddingCacheEnabled = context.Configuration
            .GetSection("AI:Rag:EmbeddingCache")
            .GetValue<bool>("Enabled");

        if (embeddingCacheEnabled)
        {
            // 将原始 EmbeddingService 注册为 keyed service，避免循环依赖
            services.AddKeyedScoped<IEmbeddingService, AI.Services.EmbeddingService>(CachingEmbeddingDecorator.InnerServiceKey);

            // 替换 IEmbeddingService 为 CachingEmbeddingDecorator
            services.RemoveAll<IEmbeddingService>();
            services.AddScoped<IEmbeddingService, CachingEmbeddingDecorator>();
        }

        // 替换 NoOpTextSearchService 为向量/混合搜索实现
        // RemoveAll 清除 AIModule 的 NoOp 回退，再注册真实实现（避免容器中残留无用注册）
        services.RemoveAll<ITextSearchService>();

        var hybridEnabled = context.Configuration
            .GetSection("AI:Rag:HybridSearch")
            .GetValue<bool>("Enabled");

        if (hybridEnabled)
        {
            // 混合搜索模式：注册关键词搜索提供者 + HybridSearchService
            services.TryAddScoped<IKeywordSearchProvider, InMemoryKeywordSearchProvider>();
            services.AddScoped<ITextSearchService, HybridSearchService>();
        }
        else
        {
            // 纯向量搜索模式（默认）
            services.AddScoped<ITextSearchService, VectorTextSearchService>();
        }

        return Task.CompletedTask;
    }
}
