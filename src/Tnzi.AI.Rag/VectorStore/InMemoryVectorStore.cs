namespace Tnzi.AI.Rag.VectorStore;

/// <summary>
/// 内存向量存储 — 用于开发和测试，无需外部数据库
/// </summary>
/// <remarks>
/// <para>
/// 使用 <see cref="ConcurrentDictionary{TKey,TValue}"/> 存储向量数据，
/// 通过余弦相似度计算进行搜索。适用于开发调试和单元测试场景，
/// 不适合生产环境（数据不持久化，重启后丢失）。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "InMemory vector store is for development/testing")]
public class InMemoryVectorStore : IVectorStore
{
    private readonly ILogger<InMemoryVectorStore> _logger;
    private readonly ConcurrentDictionary<Guid, InMemoryVectorEntry> _store = new();

    public InMemoryVectorStore(ILogger<InMemoryVectorStore> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public Task<List<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int topK,
        Guid? knowledgeBaseId = null,
        CancellationToken ct = default)
    {
        return SearchAsync(queryVector, topK, knowledgeBaseId, metadataFilter: null, ct);
    }

    /// <inheritdoc />
    public Task<List<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int topK,
        Guid? knowledgeBaseId,
        Dictionary<string, string>? metadataFilter,
        CancellationToken ct = default)
    {
        Check.NotNull(queryVector);

        var entries = knowledgeBaseId.HasValue
            ? _store.Values.Where(e => e.KnowledgeBaseId == knowledgeBaseId.Value)
            : _store.Values;

        // 应用 metadata 过滤
        if (metadataFilter is { Count: > 0 })
        {
            entries = entries.Where(e => MatchesMetadataFilter(e.Metadata, metadataFilter));
        }

        var results = entries
            .Select(e => new VectorSearchResult
            {
                Id = e.Id,
                Content = e.Content,
                DocumentId = e.DocumentId,
                KnowledgeBaseId = e.KnowledgeBaseId,
                ChunkIndex = e.ChunkIndex,
                Metadata = e.Metadata,
                Score = CosineSimilarity(queryVector, e.Embedding)
            })
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        _logger.LogDebug("InMemory vector search returned {Count} results (topK={TopK}, kbId={KbId}, store={StoreSize})",
            results.Count, topK, knowledgeBaseId, _store.Count);

        return Task.FromResult(results);
    }

    /// <inheritdoc />
    public Task UpsertAsync(Guid chunkId, float[] embedding, Guid documentId, Guid knowledgeBaseId,
        string content, int chunkIndex, string? metadata = null, CancellationToken ct = default)
    {
        Check.NotNull(embedding);
        Check.NotNull(content);

        var entry = new InMemoryVectorEntry
        {
            Id = chunkId,
            Embedding = embedding,
            DocumentId = documentId,
            KnowledgeBaseId = knowledgeBaseId,
            Content = content,
            ChunkIndex = chunkIndex,
            Metadata = metadata
        };

        _store.AddOrUpdate(chunkId, entry, (_, _) => entry);

        _logger.LogDebug("Upserted chunk {ChunkId} for document {DocumentId} in knowledge base {KnowledgeBaseId}",
            chunkId, documentId, knowledgeBaseId);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteByDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var keysToRemove = _store
            .Where(kvp => kvp.Value.DocumentId == documentId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _store.TryRemove(key, out _);
        }

        _logger.LogDebug("Deleted {Count} chunks for document {DocumentId}", keysToRemove.Count, documentId);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteByKnowledgeBaseAsync(Guid knowledgeBaseId, CancellationToken ct = default)
    {
        var keysToRemove = _store
            .Where(kvp => kvp.Value.KnowledgeBaseId == knowledgeBaseId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _store.TryRemove(key, out _);
        }

        _logger.LogDebug("Deleted {Count} chunks for knowledge base {KnowledgeBaseId}", keysToRemove.Count, knowledgeBaseId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 计算两个向量的余弦相似度
    /// </summary>
    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0.0;

        double dotProduct = 0;
        double normA = 0;
        double normB = 0;

        for (var i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * (double)b[i];
            normA += a[i] * (double)a[i];
            normB += b[i] * (double)b[i];
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);

        if (denominator == 0)
            return 0.0;

        return dotProduct / denominator;
    }

    /// <summary>
    /// 检查 metadata JSON 是否匹配过滤条件
    /// </summary>
    private static bool MatchesMetadataFilter(string? metadata, Dictionary<string, string> filter)
    {
        if (string.IsNullOrEmpty(metadata))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(metadata);
            foreach (var (key, value) in filter)
            {
                if (!doc.RootElement.TryGetProperty(key, out var prop) ||
                    prop.ToString() != value)
                {
                    return false;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 内存向量条目
    /// </summary>
    private sealed class InMemoryVectorEntry
    {
        public Guid Id { get; init; }
        public float[] Embedding { get; init; } = [];
        public string Content { get; init; } = string.Empty;
        public Guid DocumentId { get; init; }
        public Guid KnowledgeBaseId { get; init; }
        public int ChunkIndex { get; init; }
        public string? Metadata { get; init; }
    }
}
