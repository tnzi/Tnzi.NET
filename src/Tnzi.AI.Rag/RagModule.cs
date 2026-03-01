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
/// 依赖 AIModule，在其之后加载。通过 RemoveAll + AddScoped 替换 NoOpTextSearchService
/// 为 VectorTextSearchService，使 Agent 的 TextSearchProvider 获得真正的 RAG 检索能力。
/// </para>
/// </remarks>
[DependsOn(typeof(AI.AIModule))]
public class RagModule : TnziApplicationModule
{
    /// <summary>
    /// 在 AIModule(50)、AICoderModule(51) 之后加载
    /// </summary>
    public override int LoadOrder => 52;

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

        // 核心业务服务
        services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
        services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();

        // 后台摄取任务（需配合 Hangfire 模块使用，无 Hangfire 时 KnowledgeBaseService 自动回退同步模式）
        services.AddScoped<IBackgroundJob<DocumentIngestionJobArgs>, DocumentIngestionBackgroundJob>();

        // Embedding 缓存装饰器（条件启用）
        var embeddingCacheEnabled = context.Configuration
            .GetSection("AI:Rag:EmbeddingCache")
            .GetValue<bool>("Enabled");

        if (embeddingCacheEnabled)
        {
            // 包装 IEmbeddingService 为 CachingEmbeddingDecorator
            // 通过 RemoveAll + AddScoped 替换原始实现
            services.RemoveAll<IEmbeddingService>();
            services.AddScoped<IEmbeddingService>(sp =>
            {
                // 手动构造原始 EmbeddingService 避免循环依赖
                var factory = sp.GetRequiredService<IChatClientFactory>();
                var inner = new AI.Services.EmbeddingService(factory, sp);

                var decoratorLogger = sp.GetRequiredService<ILogger<CachingEmbeddingDecorator>>();
                var ragOptions = sp.GetRequiredService<IOptions<AIRagOptions>>();
                var cache = sp.GetService<Tnzi.Caching.ICache>();
                return new CachingEmbeddingDecorator(inner, decoratorLogger, ragOptions, cache);
            });
        }

        // 替换 NoOpTextSearchService 为向量/混合搜索实现
        // AIModule 使用 TryAddScoped 注册 NoOp，这里强制替换
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
