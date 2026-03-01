namespace Tnzi.AI.Rag.Observability;

/// <summary>
/// RAG 模块 ActivitySource — 用于 OpenTelemetry 追踪和指标
/// </summary>
public static class RagActivitySource
{
    /// <summary>
    /// ActivitySource 名称
    /// </summary>
    public const string Name = "Tnzi.AI.Rag";

    /// <summary>
    /// 版本号
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// ActivitySource 实例
    /// </summary>
    public static readonly ActivitySource Source = new(Name, Version);

    /// <summary>
    /// Meter 实例（用于指标）
    /// </summary>
    public static readonly Meter Meter = new(Name, Version);

    // 计数器
    private static readonly Counter<long> _ingestionCounter;
    private static readonly Counter<long> _ingestionChunkCounter;
    private static readonly Counter<long> _searchCounter;
    private static readonly Counter<long> _errorCounter;

    // Embedding 缓存计数器
    private static readonly Counter<long> _embeddingCacheHitCounter;
    private static readonly Counter<long> _embeddingCacheMissCounter;

    // 直方图
    private static readonly Histogram<double> _ingestionDurationHistogram;
    private static readonly Histogram<double> _searchDurationHistogram;

    static RagActivitySource()
    {
        _ingestionCounter = Meter.CreateCounter<long>(
            "rag.ingestion.count",
            unit: "{document}",
            description: "Number of document ingestion operations");

        _ingestionChunkCounter = Meter.CreateCounter<long>(
            "rag.ingestion.chunk.count",
            unit: "{chunk}",
            description: "Number of chunks generated during ingestion");

        _searchCounter = Meter.CreateCounter<long>(
            "rag.search.count",
            unit: "{search}",
            description: "Number of vector search operations");

        _errorCounter = Meter.CreateCounter<long>(
            "rag.error.count",
            unit: "{error}",
            description: "Number of errors in RAG operations");

        _ingestionDurationHistogram = Meter.CreateHistogram<double>(
            "rag.ingestion.duration",
            unit: "s",
            description: "Duration of document ingestion operations");

        _searchDurationHistogram = Meter.CreateHistogram<double>(
            "rag.search.duration",
            unit: "s",
            description: "Duration of vector search operations");

        _embeddingCacheHitCounter = Meter.CreateCounter<long>(
            "rag.embedding.cache.hit",
            unit: "{hit}",
            description: "Number of embedding cache hits");

        _embeddingCacheMissCounter = Meter.CreateCounter<long>(
            "rag.embedding.cache.miss",
            unit: "{miss}",
            description: "Number of embedding cache misses");
    }

    /// <summary>
    /// 开始摄取 Activity
    /// </summary>
    public static Activity? StartIngestionActivity(Guid knowledgeBaseId, string fileName)
    {
        var activity = Source.StartActivity("rag.ingest", ActivityKind.Internal);

        if (activity != null)
        {
            activity.SetTag("rag.knowledge_base.id", knowledgeBaseId.ToString());
            activity.SetTag("rag.document.file_name", fileName);
        }

        return activity;
    }

    /// <summary>
    /// 开始搜索 Activity
    /// </summary>
    public static Activity? StartSearchActivity(int topK, Guid? knowledgeBaseId = null)
    {
        var activity = Source.StartActivity("rag.search", ActivityKind.Internal);

        if (activity != null)
        {
            activity.SetTag("rag.search.top_k", topK);

            if (knowledgeBaseId.HasValue)
            {
                activity.SetTag("rag.knowledge_base.id", knowledgeBaseId.Value.ToString());
            }
        }

        return activity;
    }

    /// <summary>
    /// 记录摄取完成
    /// </summary>
    public static void RecordIngestion(Guid knowledgeBaseId, int chunkCount, double durationSeconds)
    {
        var kbTag = new KeyValuePair<string, object?>("rag.knowledge_base.id", knowledgeBaseId.ToString());

        _ingestionCounter.Add(1, kbTag);
        _ingestionChunkCounter.Add(chunkCount, kbTag);
        _ingestionDurationHistogram.Record(durationSeconds, kbTag);
    }

    /// <summary>
    /// 记录搜索完成
    /// </summary>
    public static void RecordSearch(int resultCount, double durationSeconds, Guid? knowledgeBaseId = null)
    {
        var tags = knowledgeBaseId.HasValue
            ? [new KeyValuePair<string, object?>("rag.knowledge_base.id", knowledgeBaseId.Value.ToString())]
            : Array.Empty<KeyValuePair<string, object?>>();

        _searchCounter.Add(1, tags);
        _searchDurationHistogram.Record(durationSeconds, tags);
    }

    /// <summary>
    /// 记录错误
    /// </summary>
    public static void RecordError(string operation, Exception? ex = null)
    {
        _errorCounter.Add(1,
            new KeyValuePair<string, object?>("rag.operation", operation),
            new KeyValuePair<string, object?>("error.type", ex?.GetType().FullName));
    }

    /// <summary>
    /// 记录 Embedding 缓存命中
    /// </summary>
    public static void RecordEmbeddingCacheHit()
    {
        _embeddingCacheHitCounter.Add(1);
    }

    /// <summary>
    /// 记录 Embedding 缓存未命中
    /// </summary>
    public static void RecordEmbeddingCacheMiss()
    {
        _embeddingCacheMissCounter.Add(1);
    }

    /// <summary>
    /// 记录 Activity 错误
    /// </summary>
    public static void RecordActivityError(Activity? activity, Exception exception)
    {
        if (activity == null) return;

        activity.SetTag("error.type", exception.GetType().FullName);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            ["exception.type"] = exception.GetType().FullName,
            ["exception.message"] = exception.Message,
            ["exception.stacktrace"] = exception.StackTrace
        }));
    }
}
