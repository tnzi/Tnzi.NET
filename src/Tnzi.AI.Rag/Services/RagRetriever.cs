namespace Tnzi.AI.Rag.Services;

/// <summary>
/// RAG 检索器 — 封装查询改写 → 嵌入生成 → 向量搜索 → 后处理的共享检索管线
/// <para>
/// 被 RagQueryEngine（单轮）和 RagChatEngine（多轮）共享使用，
/// 避免两个引擎各自重复实现检索逻辑。
/// </para>
/// </summary>
public class RagRetriever : ApplicationService, IRagRetriever
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IReranker _reranker;
    private readonly IRepository<KnowledgeDocument, Guid> _docRepository;
    private readonly List<ISearchPostProcessor> _sortedProcessors;
    private readonly IQueryRewriter? _queryRewriter;
    private readonly IRelevanceGrader? _relevanceGrader;

    public RagRetriever(
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
        Check.NotNull(postProcessors);
        _sortedProcessors = postProcessors.OrderBy(p => p.Order).ToList();
        _queryRewriter = queryRewriter;
        _relevanceGrader = relevanceGrader;
    }

    /// <inheritdoc />
    public async Task<List<RetrievalResult>> RetrieveAsync(string query, RagRetrievalOptions? options = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        options ??= new RagRetrievalOptions();

        try
        {
            // 1. 查询改写（可选）
            var searchQuery = query;
            if (_queryRewriter != null)
            {
                var rewritten = await _queryRewriter.RewriteAsync(query, ct);
                if (!string.IsNullOrWhiteSpace(rewritten))
                {
                    searchQuery = rewritten;
                }
            }

            // 2. 生成查询向量
            var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(searchQuery, ct: ct);
            if (!embeddingResult.Succeeded)
            {
                Logger.LogWarning("Embedding generation failed for RAG retrieval: {Message}", embeddingResult.Message);
                return [];
            }

            // 3. 向量搜索（支持多知识库）
            var allResults = new List<VectorSearchResult>();
            if (options.KnowledgeBaseIds is { Count: > 0 })
            {
                // 并行搜索多个指定知识库
                var tasks = options.KnowledgeBaseIds.Select(kbId =>
                    _vectorStore.SearchAsync(embeddingResult.Data!, options.TopK, kbId, ct));
                var resultSets = await Task.WhenAll(tasks);
                foreach (var resultSet in resultSets)
                {
                    allResults.AddRange(resultSet);
                }

                // 跨知识库结果按得分排序并截取 TopK
                allResults = allResults
                    .OrderByDescending(r => r.Score)
                    .Take(options.TopK)
                    .ToList();
            }
            else
            {
                // 搜索全部启用的知识库
                allResults = await _vectorStore.SearchAsync(embeddingResult.Data!, options.TopK, ct: ct);
            }

            // 4. 重排序
            allResults = await _reranker.RerankAsync(query, allResults, options.TopK, ct);

            // 5. 运行后处理管线（去重、归一化等）
            foreach (var processor in _sortedProcessors)
            {
                allResults = await processor.ProcessAsync(allResults, query, ct);
            }

            // 6. 按最低相关性过滤
            if (options.MinRelevance > 0)
            {
                allResults = allResults.Where(r => r.Score >= options.MinRelevance).ToList();
            }

            // 7. 相关性评分（可选）
            if (_relevanceGrader != null && allResults.Count > 0)
            {
                var textResults = allResults.Select(r => new TextSearchResult
                {
                    Text = r.Content,
                    Score = r.Score
                }).ToList();

                var graded = await _relevanceGrader.GradeAsync(query, textResults, ct);
                var relevantTexts = graded.Where(g => g.IsRelevant).Select(g => g.Result.Text).ToHashSet();
                allResults = allResults.Where(r => relevantTexts.Contains(r.Content)).ToList();
            }

            // 8. 转换为 RetrievalResult
            var results = allResults.Select(r => new RetrievalResult
            {
                Content = r.Content,
                Score = r.Score,
                KnowledgeBaseId = r.KnowledgeBaseId,
                DocumentId = r.DocumentId,
                Metadata = !string.IsNullOrWhiteSpace(r.Metadata)
                    ? JsonSerializer.Deserialize<Dictionary<string, object>>(r.Metadata)
                    : null
            }).ToList();

            Logger.LogDebug("RAG retrieval returned {Count} results for query length {Length}",
                results.Count, query.Length);

            return results;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "RAG retrieval failed for query length {Length}", query.Length);
            return [];
        }
    }

    /// <summary>
    /// 从检索结果构建引用 DTO 列表（共享方法，供 RagQueryEngine 和 RagChatEngine 使用）
    /// </summary>
    public static List<CitationDto> BuildCitations(List<RetrievalResult> results, int maxContentLength = 200)
    {
        return results.Select(r => new CitationDto
        {
            Text = r.Content.Length > maxContentLength ? r.Content[..maxContentLength] + "..." : r.Content,
            Score = r.Score
        }).ToList();
    }
}
