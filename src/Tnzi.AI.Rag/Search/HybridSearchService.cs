namespace Tnzi.AI.Rag.Search;

/// <summary>
/// 混合搜索服务 - 融合向量搜索和关键词搜索结果，使用 Reciprocal Rank Fusion (RRF) 算法
/// </summary>
/// <remarks>
/// <para>
/// 实现 <see cref="ITextSearchService"/>，在启用混合搜索时替代 <see cref="VectorTextSearchService"/>。
/// </para>
/// <para>
/// 搜索管道：
/// 1. 加载范围内仍可检索（存在且启用中）的知识库，按各库的嵌入配置分组生成查询向量
/// 2. 并行执行向量搜索（语义匹配）和关键词搜索（精确匹配）
/// 3. 使用 RRF 算法融合两路结果：score = alpha/(k + rank_vector) + beta/(k + rank_keyword)
/// 4. 按融合得分去重、排序、截取 topK
/// 5. 可选经过 <see cref="IReranker"/> 重排序
/// 6. 父文档窗口扩展（<c>ParentDocumentRetrieval.Enabled</c>）与图谱上下文片段（<c>GraphRag.Enabled</c>，仅知识库范围内）
///    —— 与 <see cref="RagRetriever"/> / <see cref="VectorTextSearchService"/> 共用 <see cref="RetrievalAugmentation"/>
/// </para>
/// <para>
/// 检索范围只来自 <see cref="TextSearchFilter.KnowledgeBaseIds"/>：向量与关键词两路都逐库检索后再融合。
/// <b>没有范围就答空</b>，与 <see cref="VectorTextSearchService"/> 同一判据（<see cref="RetrievalAugmentation.ResolveKnowledgeBaseScope"/>）：
/// 换成混合检索不能把 agent 侧的授权边界换掉。已禁用的知识库不进任何一路。
/// </para>
/// <para>
/// ★ 查询向量按库对齐（与 <see cref="RagRetriever"/> 同一分组）：此前恒用全局默认嵌入配置，建库时指定了自己 provider/model 的库
/// 在混合检索下按另一个向量空间的余弦排序，静默返回语义无关的块；不设全局默认、每库各配 provider（文档列为正常用法）时
/// 嵌入直接失败，整次检索连关键词路一起答空。现在某一组嵌入失败只让该组的向量路缺席，关键词路照常返回。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Hybrid search is in preview")]
public class HybridSearchService : ApplicationService, ITextSearchService
{
    private readonly IVectorStore _vectorStore;
    private readonly IKeywordSearchProvider _keywordSearchProvider;
    private readonly IEmbeddingService _embeddingService;
    private readonly IReranker _reranker;
    private readonly IRepository<KnowledgeBase, Guid> _kbRepository;
    private readonly IRepository<KnowledgeDocument, Guid> _docRepository;
    private readonly IRepository<DocumentChunk, Guid> _chunkRepository;
    private readonly AIRagOptions _ragOptions;
    private readonly HybridSearchOptions _hybridOptions;
    private readonly IGraphSearchService? _graphSearchService;
    private readonly IParentDocumentRetriever? _parentDocumentRetriever;

    public HybridSearchService(
        IServiceProvider serviceProvider,
        IVectorStore vectorStore,
        IKeywordSearchProvider keywordSearchProvider,
        IEmbeddingService embeddingService,
        IReranker reranker,
        IRepository<KnowledgeBase, Guid> kbRepository,
        IRepository<KnowledgeDocument, Guid> docRepository,
        IRepository<DocumentChunk, Guid> chunkRepository,
        IOptionsSnapshot<AIRagOptions> ragOptions,
        IGraphSearchService? graphSearchService = null,
        IParentDocumentRetriever? parentDocumentRetriever = null) : base(serviceProvider)
    {
        _vectorStore = Check.NotNull(vectorStore);
        _keywordSearchProvider = Check.NotNull(keywordSearchProvider);
        _embeddingService = Check.NotNull(embeddingService);
        _reranker = Check.NotNull(reranker);
        _kbRepository = Check.NotNull(kbRepository);
        _docRepository = Check.NotNull(docRepository);
        _chunkRepository = Check.NotNull(chunkRepository);
        _ragOptions = Check.NotNull(ragOptions).Value;
        _hybridOptions = _ragOptions.HybridSearch;
        _graphSearchService = graphSearchService;
        _parentDocumentRetriever = parentDocumentRetriever;
    }

    /// <inheritdoc />
    /// <remarks>不带过滤 = 没有知识库范围，恒答空（见类注释）。</remarks>
    public Task<IEnumerable<TextSearchResult>> SearchAsync(
        string query, int maxResults = 5, CancellationToken ct = default)
        => SearchAsync(query, filter: null, maxResults, ct);

    /// <inheritdoc />
    public Task<IEnumerable<TextSearchResult>> SearchAsync(
        string query, TextSearchFilter? filter, int maxResults = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IEnumerable<TextSearchResult>>([]);
        }

        var knowledgeBaseIds = RetrievalAugmentation.ResolveKnowledgeBaseScope(filter);
        if (knowledgeBaseIds is null)
        {
            Logger.LogDebug("HybridSearchService skipped a search without a knowledge base scope it can enforce");
            return Task.FromResult<IEnumerable<TextSearchResult>>([]);
        }

        return SearchCoreAsync(query, knowledgeBaseIds, maxResults, ct);
    }

    private async Task<IEnumerable<TextSearchResult>> SearchCoreAsync(
        string query, IReadOnlyList<Guid> requestedKbIds, int maxResults, CancellationToken ct)
    {
        try
        {
            using var activity = RagActivitySource.StartSearchActivity(maxResults);
            var sw = Stopwatch.StartNew();

            // 1. 范围内仍可检索的知识库（存在且启用中），按各库的嵌入配置生成查询向量。
            //    这几步都要顺序做：加载知识库走本 scope 的 RagDbContext，关键词提供者也可能走它。
            var knowledgeBases = await RetrievalAugmentation.LoadSearchableKnowledgeBasesAsync(
                _kbRepository, requestedKbIds, Logger, ct);
            if (knowledgeBases.Count == 0)
            {
                return [];
            }

            var kbIds = knowledgeBases.Select(kb => kb.Id).ToList();
            var queryVectors = await GenerateQueryVectorsAsync(query, knowledgeBases, ct);

            // 2. 并行执行向量搜索和关键词搜索（请求双倍数量以便融合后仍有足够结果）。
            //    两路都逐库检索后合并（关键词提供者按库过滤走 DB 侧，向量路径亦然）。
            var fetchCount = maxResults * 2;
            var vectorTask = SearchVectorAsync(queryVectors, fetchCount, ct);
            var keywordTask = SearchKeywordAsync(query, fetchCount, kbIds, ct);

            await Task.WhenAll(vectorTask, keywordTask);

            var vectorResults = await vectorTask;
            var keywordResults = await ResolveMissingChunkIndexesAsync(await keywordTask, ct);

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

            // 5. 父文档窗口扩展 + 图谱片段（与 RagRetriever 同一份实现；图谱只在知识库范围内）
            var retrievalResults = fusedResults.Select(r => new RetrievalResult
            {
                Content = r.Content,
                Score = r.Score,
                KnowledgeBaseId = r.KnowledgeBaseId,
                DocumentId = r.DocumentId,
                Metadata = new Dictionary<string, object> { ["chunkIndex"] = r.ChunkIndex, ["searchType"] = "hybrid" }
            }).ToList();
            retrievalResults = await RetrievalAugmentation.ExpandParentsAsync(
                _parentDocumentRetriever, retrievalResults, _ragOptions, enabledOverride: null, Logger, ct);
            var graphResults = await RetrievalAugmentation.SearchGraphAsync(_graphSearchService, query, kbIds, Logger, ct);
            retrievalResults = RetrievalAugmentation.AppendGraphSnippets(retrievalResults, graphResults);

            // 6. 获取文档名映射并转换为 TextSearchResult
            var docIds = retrievalResults.Select(r => r.DocumentId).Where(id => id != Guid.Empty).Distinct().ToList();
            var docs = docIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await _docRepository.AsQueryable()
                    .Where(d => docIds.Contains(d.Id))
                    .ToDictionaryAsync(d => d.Id, d => d.FileName, ct);
            var searchResults = RetrievalAugmentation.ToTextSearchResults(retrievalResults, docs);

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
    /// 按 (provider, model) 分组为每组生成一次查询向量（与摄取、<see cref="RagRetriever"/> 同一解析）。
    /// 某组失败只让该组的库缺席向量路，记一条警告；关键词路不受影响。
    /// </summary>
    private async Task<List<(float[] Vector, List<Guid> KnowledgeBaseIds)>> GenerateQueryVectorsAsync(
        string query, IReadOnlyList<KnowledgeBase> knowledgeBases, CancellationToken ct)
    {
        var queryVectors = new List<(float[] Vector, List<Guid> KnowledgeBaseIds)>();
        foreach (var (embeddingOptions, groupKbIds) in RagEmbeddingOptionsResolver.GroupByEmbeddingConfig(knowledgeBases, _ragOptions))
        {
            var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(query, embeddingOptions, ct);
            if (!embeddingResult.Succeeded)
            {
                Logger.LogWarning(
                    "Embedding generation failed for hybrid search (provider={Provider}, model={Model}); {Count} knowledge bases are searched by keyword only: {Message}",
                    embeddingOptions.Provider, embeddingOptions.Model, groupKbIds.Count, embeddingResult.Message);
                continue;
            }

            queryVectors.Add((embeddingResult.Data!, groupKbIds));
        }

        return queryVectors;
    }

    private async Task<List<VectorSearchResult>> SearchVectorAsync(
        IReadOnlyList<(float[] Vector, List<Guid> KnowledgeBaseIds)> queryVectors, int fetchCount, CancellationToken ct)
    {
        var merged = new List<VectorSearchResult>();
        foreach (var (vector, groupKbIds) in queryVectors)
        {
            foreach (var kbId in groupKbIds)
            {
                merged.AddRange(await _vectorStore.SearchAsync(vector, fetchCount, kbId, ct));
            }
        }
        return merged.OrderByDescending(r => r.Score).Take(fetchCount).ToList();
    }

    private async Task<List<KeywordSearchResult>> SearchKeywordAsync(string query, int fetchCount, List<Guid> kbIds, CancellationToken ct)
    {
        var merged = new List<KeywordSearchResult>();
        foreach (var kbId in kbIds)
        {
            merged.AddRange(await _keywordSearchProvider.SearchAsync(query, fetchCount, kbId, ct));
        }
        return merged.OrderByDescending(r => r.Score).Take(fetchCount).ToList();
    }

    /// <summary>
    /// 提供者没给出 <see cref="KeywordSearchResult.ChunkIndex"/> 的命中，按 <see cref="KeywordSearchResult.ChunkId"/> 一次回查分块表补上；
    /// 分块表里已经没有的命中（文档刚被删）丢弃。框架自带的两个提供者都直接给出，不会走到回查。
    /// </summary>
    /// <remarks>
    /// 不能「不知道就不写 chunkIndex」：父文档检索丢掉没有 chunkIndex 的结果、再用扩展结果整体替换原列表，关键词命中会直接消失。
    /// </remarks>
    private async Task<List<KeywordSearchResult>> ResolveMissingChunkIndexesAsync(List<KeywordSearchResult> keywordResults, CancellationToken ct)
    {
        var missing = keywordResults.Where(r => r.ChunkIndex is null).Select(r => r.ChunkId).Distinct().ToList();
        if (missing.Count == 0)
        {
            return keywordResults;
        }

        var indexes = await _chunkRepository.AsQueryable()
            .Where(c => missing.Contains(c.Id))
            .Select(c => new { c.Id, c.ChunkIndex })
            .ToDictionaryAsync(c => c.Id, c => c.ChunkIndex, ct);

        return keywordResults
            .Where(r => r.ChunkIndex is not null || indexes.ContainsKey(r.ChunkId))
            .Select(r => r.ChunkIndex is not null ? r : new KeywordSearchResult
            {
                ChunkId = r.ChunkId,
                Content = r.Content,
                DocumentId = r.DocumentId,
                KnowledgeBaseId = r.KnowledgeBaseId,
                ChunkIndex = indexes[r.ChunkId],
                Score = r.Score
            })
            .ToList();
    }

    /// <summary>
    /// Reciprocal Rank Fusion (RRF) - 融合向量搜索和关键词搜索结果
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
                // 仅关键词搜索有此块：块索引取提供者给的（经 ResolveMissingChunkIndexesAsync 补齐）
                fusedScores[result.ChunkId] = new FusedEntry
                {
                    VectorResult = new VectorSearchResult
                    {
                        Id = result.ChunkId,
                        Content = result.Content,
                        DocumentId = result.DocumentId,
                        KnowledgeBaseId = result.KnowledgeBaseId,
                        ChunkIndex = result.ChunkIndex ?? 0
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
