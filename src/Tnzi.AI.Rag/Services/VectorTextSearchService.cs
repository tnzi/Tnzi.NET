namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 基于向量的文本搜索服务 — 替换 NoOpTextSearchService，
/// 为 Agent 的 TextSearchProvider 提供 RAG 检索能力
/// </summary>
/// <remarks>
/// <para>
/// 搜索管线：查询改写 → 生成嵌入 → 向量搜索 → 重排序 → 相关性评分 → 结果映射
/// </para>
/// </remarks>
public class VectorTextSearchService : ApplicationService, ITextSearchService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IReranker _reranker;
    private readonly IRepository<KnowledgeDocument, Guid> _docRepository;
    private readonly IQueryRewriter? _queryRewriter;
    private readonly IRelevanceGrader? _relevanceGrader;
    private readonly IEnumerable<ISearchPostProcessor> _postProcessors;

    public VectorTextSearchService(
        IServiceProvider serviceProvider,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IReranker reranker,
        IRepository<KnowledgeDocument, Guid> docRepository,
        IEnumerable<ISearchPostProcessor> postProcessors,
        IQueryRewriter? queryRewriter = null,
        IRelevanceGrader? relevanceGrader = null) : base(serviceProvider)
    {
        _embeddingService = Check.NotNull(embeddingService);
        _vectorStore = Check.NotNull(vectorStore);
        _reranker = Check.NotNull(reranker);
        _docRepository = Check.NotNull(docRepository);
        _postProcessors = Check.NotNull(postProcessors);
        _queryRewriter = queryRewriter;
        _relevanceGrader = relevanceGrader;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TextSearchResult>> SearchAsync(
        string query,
        int maxResults = 5,
        CancellationToken ct = default)
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

            // 2. 生成查询向量
            var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(searchQuery, ct: ct);
            if (!embeddingResult.Succeeded)
            {
                Logger.LogWarning("Embedding generation failed for text search: {Message}", embeddingResult.Message);
                return [];
            }

            // 3. 向量搜索（跨所有启用的知识库）+ 重排序
            var rawResults = await _vectorStore.SearchAsync(embeddingResult.Data!, maxResults, ct: ct);
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

            // 6. 相关性评分（可选）— 过滤不相关的结果
            if (_relevanceGrader != null)
            {
                var graded = await _relevanceGrader.GradeAsync(query, searchResults, ct);
                searchResults = graded.Where(g => g.IsRelevant).Select(g => g.Result).ToList();
            }

            Logger.LogDebug("VectorTextSearchService returned {Count} results for query length {Length}",
                searchResults.Count, query.Length);

            return searchResults;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Vector text search failed for query length {Length}", query.Length);
            return [];
        }
    }
}
