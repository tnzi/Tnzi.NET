namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 基于向量的文本搜索服务 - 替换 NoOpTextSearchService，
/// 为 Agent 的 TextSearchProvider 提供 RAG 检索能力
/// </summary>
/// <remarks>
/// <para>
/// 搜索管线：查询改写 → 生成嵌入 → 向量搜索 → 重排序 → 相关性评分 → 结果映射
/// </para>
/// <para>
/// 支持按知识库范围过滤：当 <see cref="TextSearchFilter.KnowledgeBaseIds"/> 非空时，检索仅限
/// 这些知识库（逐库查询后合并重排）；为空时跨所有启用知识库（向后兼容）。
/// 该范围由 Agent 的 <c>KnowledgeBaseIds</c> 分配经 TextSearchProvider 传入。
/// </para>
/// </remarks>
public class VectorTextSearchService : ApplicationService, ITextSearchService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IReranker _reranker;
    private readonly IRepository<KnowledgeDocument, Guid> _docRepository;
    private readonly IRepository<KnowledgeBase, Guid> _kbRepository;
    private readonly AIRagOptions _ragOptions;
    private readonly IQueryRewriter? _queryRewriter;
    private readonly IRelevanceGrader? _relevanceGrader;
    private readonly IEnumerable<ISearchPostProcessor> _postProcessors;

    public VectorTextSearchService(
        IServiceProvider serviceProvider,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IReranker reranker,
        IRepository<KnowledgeDocument, Guid> docRepository,
        IRepository<KnowledgeBase, Guid> kbRepository,
        IEnumerable<ISearchPostProcessor> postProcessors,
        IOptionsSnapshot<AIRagOptions> ragOptions,
        IQueryRewriter? queryRewriter = null,
        IRelevanceGrader? relevanceGrader = null) : base(serviceProvider)
    {
        _embeddingService = Check.NotNull(embeddingService);
        _vectorStore = Check.NotNull(vectorStore);
        _reranker = Check.NotNull(reranker);
        _docRepository = Check.NotNull(docRepository);
        _kbRepository = Check.NotNull(kbRepository);
        _postProcessors = Check.NotNull(postProcessors);
        _ragOptions = Check.NotNull(ragOptions).Value;
        _queryRewriter = queryRewriter;
        _relevanceGrader = relevanceGrader;
    }

    /// <inheritdoc />
    public Task<IEnumerable<TextSearchResult>> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken ct = default)
        => SearchCoreAsync(query, knowledgeBaseIds: null, maxResults, ct);

    /// <inheritdoc />
    public Task<IEnumerable<TextSearchResult>> SearchAsync(
        string query,
        TextSearchFilter? filter,
        int maxResults = 5,
        CancellationToken ct = default)
        => SearchCoreAsync(query, filter?.KnowledgeBaseIds, maxResults, ct);

    private async Task<IEnumerable<TextSearchResult>> SearchCoreAsync(
        string query,
        IReadOnlyList<Guid>? knowledgeBaseIds,
        int maxResults,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        try
        {
            // 1. 查询改写（可选）
            var searchQuery = query;
            if (_queryRewriter != null)
            {
                searchQuery = await _queryRewriter.RewriteAsync(query, ct);
                if (string.IsNullOrWhiteSpace(searchQuery))
                {
                    searchQuery = query;
                }
            }

            // 2+3. 生成查询向量并执行向量搜索 - 有知识库范围时按 per-KB 嵌入配置分组逐库检索后合并；
            // 否则使用全局默认嵌入配置跨所有启用知识库（query 向量须与摄取 provider/model 对齐，
            // 与 KnowledgeBaseService.SearchAsync 同范式）。
            List<VectorSearchResult> rawResults;
            if (knowledgeBaseIds is { Count: > 0 })
            {
                var kbIds = knowledgeBaseIds.Distinct().ToList();
                var knowledgeBases = await _kbRepository.AsQueryable()
                    .Where(kb => kbIds.Contains(kb.Id))
                    .ToListAsync(ct);

                if (knowledgeBases.Count < kbIds.Count)
                {
                    Logger.LogWarning(
                        "Vector text search skipped {Missing} of {Requested} requested knowledge bases (not found or not accessible)",
                        kbIds.Count - knowledgeBases.Count, kbIds.Count);
                }

                var merged = new List<VectorSearchResult>();
                foreach (var (embeddingOptions, groupKbIds) in RagEmbeddingOptionsResolver.GroupByEmbeddingConfig(knowledgeBases, _ragOptions))
                {
                    var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(searchQuery, embeddingOptions, ct);
                    if (!embeddingResult.Succeeded)
                    {
                        Logger.LogWarning(
                            "Embedding generation failed for text search (provider={Provider}, model={Model}): {Message}",
                            embeddingOptions.Provider, embeddingOptions.Model, embeddingResult.Message);
                        continue;
                    }

                    foreach (var kbId in groupKbIds)
                    {
                        var perKb = await _vectorStore.SearchAsync(embeddingResult.Data!, maxResults, kbId, ct);
                        merged.AddRange(perKb);
                    }
                }

                // 合并后按分数降序取候选交给重排（重排会二次裁剪到 maxResults）
                rawResults = merged.OrderByDescending(r => r.Score).Take(maxResults * kbIds.Count).ToList();
            }
            else
            {
                var defaultOptions = RagEmbeddingOptionsResolver.ResolveDefault(_ragOptions);
                var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(searchQuery, defaultOptions, ct);
                if (!embeddingResult.Succeeded)
                {
                    Logger.LogWarning("Embedding generation failed for text search: {Message}", embeddingResult.Message);
                    return [];
                }

                rawResults = await _vectorStore.SearchAsync(embeddingResult.Data!, maxResults, ct: ct);
            }

            var results = await _reranker.RerankAsync(query, rawResults, maxResults, ct);

            // 3.5 Run post-processors in order
            var orderedProcessors = _postProcessors.OrderBy(p => p.Order).ToList();
            foreach (var processor in orderedProcessors)
            {
                results = await processor.ProcessAsync(results, query, ct);
            }

            if (results.Count == 0)
            {
                return [];
            }

            // 4. 获取文档名映射
            var docIds = results.Select(r => r.DocumentId).Distinct().ToList();
            var docs = await _docRepository.AsQueryable()
                .Where(d => docIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.FileName, ct);

            // 5. 转换为 TextSearchResult
            var searchResults = results.Select(r => new TextSearchResult
            {
                Text = r.Content,
                SourceName = docs.GetValueOrDefault(r.DocumentId),
                Score = r.Score,
                Metadata = new Dictionary<string, object?>
                {
                    ["chunkIndex"] = r.ChunkIndex,
                    ["documentId"] = r.DocumentId,
                    ["knowledgeBaseId"] = r.KnowledgeBaseId
                }
            }).ToList();

            // 6. 相关性评分（可选）- 过滤不相关的结果
            if (_relevanceGrader != null)
            {
                var graded = await _relevanceGrader.GradeAsync(query, searchResults, ct);
                searchResults = graded.Where(g => g.IsRelevant).Select(g => g.Result).ToList();
            }

            Logger.LogDebug("VectorTextSearchService returned {Count} results for query length {Length} (kbScope={KbScope})",
                searchResults.Count, query.Length, knowledgeBaseIds is { Count: > 0 } ? knowledgeBaseIds.Count : 0);

            return searchResults;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Vector text search failed for query length {Length}", query.Length);
            return [];
        }
    }
}
