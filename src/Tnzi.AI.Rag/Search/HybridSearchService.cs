namespace Tnzi.AI.Rag.Search;

/// <summary>
/// 混合搜索服务 — 融合向量搜索和关键词搜索结果，使用 Reciprocal Rank Fusion (RRF) 算法
/// </summary>
/// <remarks>
/// <para>
/// 实现 <see cref="ITextSearchService"/>，在启用混合搜索时替代 <see cref="VectorTextSearchService"/>。
/// </para>
/// <para>
/// 搜索管道：
/// 1. 并行执行向量搜索（语义匹配）和关键词搜索（精确匹配）
/// 2. 使用 RRF 算法融合两路结果：score = alpha/(k + rank_vector) + beta/(k + rank_keyword)
/// 3. 按融合得分去重、排序、截取 topK
/// 4. 可选经过 <see cref="IReranker"/> 重排序
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Hybrid search is in preview")]
public class HybridSearchService : ApplicationService, ITextSearchService
{
    private readonly IVectorStore _vectorStore;
    private readonly IKeywordSearchProvider _keywordSearchProvider;
    private readonly IEmbeddingService _embeddingService;
    private readonly IReranker _reranker;
    private readonly IRepository<KnowledgeDocument, Guid> _docRepository;
    private readonly HybridSearchOptions _hybridOptions;

    public HybridSearchService(
        IServiceProvider serviceProvider,
        IVectorStore vectorStore,
        IKeywordSearchProvider keywordSearchProvider,
        IEmbeddingService embeddingService,
        IReranker reranker,
        IRepository<KnowledgeDocument, Guid> docRepository,
        IOptions<AIRagOptions> ragOptions) : base(serviceProvider)
    {
        _vectorStore = Check.NotNull(vectorStore);
        _keywordSearchProvider = Check.NotNull(keywordSearchProvider);
        _embeddingService = Check.NotNull(embeddingService);
        _reranker = Check.NotNull(reranker);
        _docRepository = Check.NotNull(docRepository);
        _hybridOptions = Check.NotNull(ragOptions).Value.HybridSearch;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TextSearchResult>> SearchAsync(
        string query, int maxResults = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        try
        {
            using var activity = RagActivitySource.StartSearchActivity(maxResults);
            var sw = Stopwatch.StartNew();

            // 1. 生成查询向量
            var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(query, ct: ct);
            if (!embeddingResult.Succeeded)
            {
                Logger.LogWarning("Embedding generation failed for hybrid search: {Message}", embeddingResult.Message);
                return [];
            }

            // 2. 并行执行向量搜索和关键词搜索（请求双倍数量以便融合后仍有足够结果）
            var fetchCount = maxResults * 2;
            var vectorTask = _vectorStore.SearchAsync(embeddingResult.Data!, fetchCount, ct: ct);
            var keywordTask = _keywordSearchProvider.SearchAsync(query, fetchCount, ct: ct);

            await Task.WhenAll(vectorTask, keywordTask);

            var vectorResults = await vectorTask;
            var keywordResults = await keywordTask;

            Logger.LogDebug(
                "Hybrid search fetched {VectorCount} vector results and {KeywordCount} keyword results",
                vectorResults.Count, keywordResults.Count);

            // 3. RRF 融合
            var fusedResults = ReciprocalRankFusion(vectorResults, keywordResults, maxResults);

            // 4. Reranker 重排序（与 VectorTextSearchService 保持一致）
            if (fusedResults.Count > 0)
            {
                fusedResults = await _reranker.RerankAsync(query, fusedResults, maxResults, ct);
            }

            if (fusedResults.Count == 0)
            {
                return [];
            }

            // 5. 获取文档名映射
            var docIds = fusedResults.Select(r => r.DocumentId).Distinct().ToList();
            var docs = await _docRepository.AsQueryable()
                .Where(d => docIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.FileName, ct);

            // 6. 转换为 TextSearchResult
            var searchResults = fusedResults.Select(r => new TextSearchResult
            {
                Text = r.Content,
                SourceName = docs.GetValueOrDefault(r.DocumentId),
                Score = r.Score,
                Metadata = new Dictionary<string, object?>
                {
                    ["chunkIndex"] = r.ChunkIndex,
                    ["documentId"] = r.DocumentId,
                    ["knowledgeBaseId"] = r.KnowledgeBaseId,
                    ["searchType"] = "hybrid"
                }
            }).ToList();

            sw.Stop();
            RagActivitySource.RecordSearch(searchResults.Count, sw.Elapsed.TotalSeconds);

            Logger.LogDebug(
                "HybridSearchService returned {Count} results for query length {Length} ({Duration:F3}s)",
                searchResults.Count, query.Length, sw.Elapsed.TotalSeconds);

            return searchResults;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Hybrid search failed for query length {Length}", query.Length);
            RagActivitySource.RecordError("hybrid_search", ex);
            return [];
        }
    }

    /// <summary>
    /// Reciprocal Rank Fusion (RRF) — 融合向量搜索和关键词搜索结果
    /// </summary>
    /// <param name="vectorResults">向量搜索结果</param>
    /// <param name="keywordResults">关键词搜索结果</param>
    /// <param name="topK">最终返回的最大结果数</param>
    /// <returns>融合并去重后的结果</returns>
    /// <remarks>This method is public for testability but is not part of the module's public API surface.</remarks>
    public List<VectorSearchResult> ReciprocalRankFusion(
        List<VectorSearchResult> vectorResults,
        List<KeywordSearchResult> keywordResults,
        int topK)
    {
        var alpha = _hybridOptions.VectorWeight;
        var beta = _hybridOptions.KeywordWeight;
        var k = _hybridOptions.FusionConstantK;

        // 用 ChunkId 作为去重键，累加 RRF 分数
        var fusedScores = new Dictionary<Guid, FusedEntry>();

        // 向量搜索结果的 RRF 分数
        for (var rank = 0; rank < vectorResults.Count; rank++)
        {
            var result = vectorResults[rank];
            var rrfScore = alpha / (k + rank + 1); // rank 从 1 开始

            fusedScores[result.Id] = new FusedEntry
            {
                VectorResult = result,
                Score = rrfScore
            };
        }

        // 关键词搜索结果的 RRF 分数
        for (var rank = 0; rank < keywordResults.Count; rank++)
        {
            var result = keywordResults[rank];
            var rrfScore = beta / (k + rank + 1);

            if (fusedScores.TryGetValue(result.ChunkId, out var existing))
            {
                // 两路都有此块，累加分数
                existing.Score += rrfScore;
            }
            else
            {
                // 仅关键词搜索有此块
                fusedScores[result.ChunkId] = new FusedEntry
                {
                    VectorResult = new VectorSearchResult
                    {
                        Id = result.ChunkId,
                        Content = result.Content,
                        DocumentId = result.DocumentId,
                        KnowledgeBaseId = result.KnowledgeBaseId,
                        ChunkIndex = 0 // 关键词搜索不返回 ChunkIndex
                    },
                    Score = rrfScore
                };
            }
        }

        // 按融合分数降序排列，取 topK
        return fusedScores.Values
            .OrderByDescending(e => e.Score)
            .Take(topK)
            .Select(e => new VectorSearchResult
            {
                Id = e.VectorResult.Id,
                Content = e.VectorResult.Content,
                DocumentId = e.VectorResult.DocumentId,
                KnowledgeBaseId = e.VectorResult.KnowledgeBaseId,
                ChunkIndex = e.VectorResult.ChunkIndex,
                Metadata = e.VectorResult.Metadata,
                ParentChunkId = e.VectorResult.ParentChunkId,
                Score = e.Score
            })
            .ToList();
    }

    /// <summary>
    /// RRF 融合中间结果
    /// </summary>
    private sealed class FusedEntry
    {
        public VectorSearchResult VectorResult { get; set; } = null!;
        public double Score { get; set; }
    }
}
