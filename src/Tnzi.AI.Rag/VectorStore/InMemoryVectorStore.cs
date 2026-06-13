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
/// <para>
/// 多租户隔离与 <see cref="PgVectorStore"/> 对齐：upsert 时记录当前租户 ID，
/// 搜索时若存在非空租户上下文则只返回该租户的条目；无租户上下文（单租户 / host）不过滤。
/// 多租户启用且无租户上下文时，无 KB 范围限定的跨库搜索经
/// <see cref="RagTenantSqlFilter.EnsureTenantScopeForSearchAll"/> fail-closed 拒绝。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "InMemory vector store is for development/testing")]
public class InMemoryVectorStore : IVectorStore
{
    private readonly ILogger<InMemoryVectorStore> _logger;
    private readonly IServiceProvider? _serviceProvider;
    private readonly bool _multiTenancyEnabled;
    private readonly ConcurrentDictionary<Guid, InMemoryVectorEntry> _store = new();

    public InMemoryVectorStore(
        ILogger<InMemoryVectorStore> logger,
        IServiceProvider? serviceProvider = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
    {
        _logger = Check.NotNull(logger);
        _serviceProvider = serviceProvider;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    /// <summary>
    /// 解析当前租户 ID。
    /// <para>
    /// 与 <see cref="PgVectorStore.ResolveTenantId"/> 同模式：本类注册为 Singleton，而
    /// <c>ICurrentTenant</c> 是 Scoped，不能在构造时持有引用（captive dependency）。
    /// 通过根 <c>IServiceProvider</c> 创建 scope 动态解析。服务提供者为 null
    /// （直接 new 的测试场景）时视为无租户上下文。
    /// </para>
    /// </summary>
    private Guid? ResolveTenantId()
    {
        if (_serviceProvider == null)
        {
            return null;
        }

        using var scope = _serviceProvider.CreateScope();
        return scope.ServiceProvider.GetService<ICurrentTenant>()?.Id;
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

        // 多租户隔离：与 PgVectorStore 的 raw-SQL 路径同语义 —— 有租户上下文则过滤，
        // MT 启用且无租户上下文时禁止无 KB 范围的跨库搜索（fail-closed）。
        var tenantId = ResolveTenantId();
        RagTenantSqlFilter.EnsureTenantScopeForSearchAll(_multiTenancyEnabled, tenantId, knowledgeBaseId.HasValue);

        var entries = _store.Values.AsEnumerable();

        if (tenantId is { } currentTenantId)
        {
            entries = entries.Where(e => e.TenantId == currentTenantId);
        }

        if (knowledgeBaseId.HasValue)
        {
            entries = entries.Where(e => e.KnowledgeBaseId == knowledgeBaseId.Value);
        }

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
            Metadata = metadata,
            // 与 EF 写入路径的 IMultiTenant 自动填充对齐：upsert 时记录当前租户
            TenantId = ResolveTenantId()
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
        public Guid? TenantId { get; init; }
    }
}
