namespace Tnzi.AI.Rag.Services;

/// <summary>
/// RAG 检索器 - 封装查询改写 → 嵌入生成 → 向量搜索 → 图谱搜索 → 后处理的共享检索管线
/// <para>
/// 被 RagQueryEngine（单轮）和 RagChatEngine（多轮）共享使用，
/// 避免两个引擎各自重复实现检索逻辑。
/// </para>
/// <para>
/// 当 <see cref="IGraphSearchService"/> 可用时，先做向量搜索、再做图谱搜索（两者都经同一个
/// scoped DbContext 查库，不能并行，见 <c>RetrieveAsync</c> 内注释），
/// 图谱搜索结果作为附加上下文片段追加到向量检索结果之后（不参与向量结果的排序）。
/// </para>
/// </summary>
public class RagRetriever : ApplicationService, IRagRetriever
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IReranker _reranker;
    private readonly IRepository<KnowledgeBase, Guid> _kbRepository;
    private readonly List<ISearchPostProcessor> _sortedProcessors;
    private readonly AIRagOptions _ragOptions;
    private readonly IQueryRewriter? _queryRewriter;
    private readonly IRelevanceGrader? _relevanceGrader;
    private readonly IGraphSearchService? _graphSearchService;
    private readonly IParentDocumentRetriever? _parentDocumentRetriever;

    public RagRetriever(
        IServiceProvider serviceProvider,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IReranker reranker,
        IRepository<KnowledgeBase, Guid> kbRepository,
        IEnumerable<ISearchPostProcessor> postProcessors,
        IOptionsSnapshot<AIRagOptions> ragOptions,
        IQueryRewriter? queryRewriter = null,
        IRelevanceGrader? relevanceGrader = null,
        IGraphSearchService? graphSearchService = null,
        IParentDocumentRetriever? parentDocumentRetriever = null) : base(serviceProvider)
    {
        _embeddingService = Check.NotNull(embeddingService);
        _vectorStore = Check.NotNull(vectorStore);
        _reranker = Check.NotNull(reranker);
        _kbRepository = Check.NotNull(kbRepository);
        Check.NotNull(postProcessors);
        _sortedProcessors = postProcessors.OrderBy(p => p.Order).ToList();
        _ragOptions = Check.NotNull(ragOptions).Value;
        _queryRewriter = queryRewriter;
        _relevanceGrader = relevanceGrader;
        _graphSearchService = graphSearchService;
        _parentDocumentRetriever = parentDocumentRetriever;
    }

    private RagRetrievalOptions ClampTopK(RagRetrievalOptions options)
    {
        var clamped = Math.Clamp(options.TopK, 1, _ragOptions.MaxTopK);
        return clamped == options.TopK
            ? options
            : new RagRetrievalOptions
            {
                KnowledgeBaseIds = options.KnowledgeBaseIds,
                TopK = clamped,
                MinRelevance = options.MinRelevance,
                EnableParentRetrieval = options.EnableParentRetrieval
            };
    }

    /// <inheritdoc />
    public async Task<List<RetrievalResult>> RetrieveAsync(string query, RagRetrievalOptions? options = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        // 纵深防御：引擎已 clamp，但绕过引擎直接用检索器的调用方同样不得把整库拉进内存。
        // 复制一份而不是改调用方的对象。
        options = ClampTopK(options ?? new RagRetrievalOptions());

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

            // 2+3. 向量搜索（含 per-KB 对齐的查询向量生成，支持多知识库）→ 图谱搜索。
            // 必须顺序执行：两者都经本 scope 的 RagDbContext 查库（向量路径读 KnowledgeBase 取
            // per-KB 嵌入配置，图谱路径读 KnowledgeGraphNode/Edge），并发使用同一 DbContext 会触发
            // "A second operation was started on this context" 并被下方 catch 吞成空结果。
            var allResults = await SearchVectorAsync(searchQuery, options, ct);
            var graphResults = await RetrievalAugmentation.SearchGraphAsync(
                _graphSearchService, searchQuery, options.KnowledgeBaseIds, Logger, ct);

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

            // 8. 转换为 RetrievalResult（始终包含 chunkIndex 以支持 Parent Document Retrieval）
            var results = allResults.Select(r =>
            {
                var metadata = !string.IsNullOrWhiteSpace(r.Metadata)
                    ? JsonSerializer.Deserialize<Dictionary<string, object>>(r.Metadata)
                    : new Dictionary<string, object>();
                metadata ??= new Dictionary<string, object>();
                metadata.TryAdd("chunkIndex", r.ChunkIndex);
                return new RetrievalResult
                {
                    Content = r.Content,
                    Score = r.Score,
                    KnowledgeBaseId = r.KnowledgeBaseId,
                    DocumentId = r.DocumentId,
                    Metadata = metadata
                };
            }).ToList();

            // 9. Parent Document Retrieval（可选：将细粒度匹配块扩展为更大的上下文窗口）
            results = await RetrievalAugmentation.ExpandParentsAsync(
                _parentDocumentRetriever, results, _ragOptions, options.EnableParentRetrieval, Logger, ct);

            // 10. 追加图谱搜索上下文片段（不参与向量结果排序，作为补充上下文）
            results = RetrievalAugmentation.AppendGraphSnippets(results, graphResults);

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

    /// <summary>
    /// 执行向量搜索（含查询向量生成，支持多知识库并行）。
    /// <para>
    /// query 向量必须与各知识库摄取时使用相同的 embedding provider/model，否则向量空间不一致、
    /// 检索结果无意义（与 <c>KnowledgeBaseService.SearchAsync</c> 同范式）。多 KB 且嵌入配置不一致时，
    /// 按 (provider, model) 分组逐组生成 query 向量后分别检索；未指定 KB（search-all）时使用全局默认配置。
    /// </para>
    /// </summary>
    private async Task<List<VectorSearchResult>> SearchVectorAsync(
        string searchQuery, RagRetrievalOptions options, CancellationToken ct)
    {
        if (options.KnowledgeBaseIds is { Count: > 0 })
        {
            // 加载 KB 嵌入配置（经 EF 全局过滤器自动应用租户隔离；缺失的 KB 直接跳过）
            var kbIds = options.KnowledgeBaseIds.Distinct().ToList();
            var knowledgeBases = await _kbRepository.AsQueryable()
                .Where(kb => kbIds.Contains(kb.Id))
                .ToListAsync(ct);

            if (knowledgeBases.Count < kbIds.Count)
            {
                Logger.LogWarning(
                    "RAG retrieval skipped {Missing} of {Requested} requested knowledge bases (not found or not accessible)",
                    kbIds.Count - knowledgeBases.Count, kbIds.Count);
            }

            var allResults = new List<VectorSearchResult>();

            // 按嵌入配置分组：同空间共用一个 query 向量，组内多 KB 并行检索
            foreach (var (embeddingOptions, groupKbIds) in RagEmbeddingOptionsResolver.GroupByEmbeddingConfig(knowledgeBases, _ragOptions))
            {
                var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(searchQuery, embeddingOptions, ct);
                if (!embeddingResult.Succeeded)
                {
                    Logger.LogWarning(
                        "Embedding generation failed for RAG retrieval (provider={Provider}, model={Model}): {Message}",
                        embeddingOptions.Provider, embeddingOptions.Model, embeddingResult.Message);
                    continue;
                }

                var tasks = groupKbIds.Select(kbId =>
                    _vectorStore.SearchAsync(embeddingResult.Data!, options.TopK, kbId, ct));
                var resultSets = await Task.WhenAll(tasks);
                foreach (var resultSet in resultSets)
                {
                    allResults.AddRange(resultSet);
                }
            }

            // 跨知识库结果按得分排序并截取 TopK
            return allResults
                .OrderByDescending(r => r.Score)
                .Take(options.TopK)
                .ToList();
        }

        // 搜索全部启用的知识库：使用全局默认 embedding provider/model
        var defaultOptions = RagEmbeddingOptionsResolver.ResolveDefault(_ragOptions);
        var defaultEmbedding = await _embeddingService.GenerateEmbeddingAsync(searchQuery, defaultOptions, ct);
        if (!defaultEmbedding.Succeeded)
        {
            Logger.LogWarning("Embedding generation failed for RAG retrieval: {Message}", defaultEmbedding.Message);
            return [];
        }

        return await _vectorStore.SearchAsync(defaultEmbedding.Data!, options.TopK, ct: ct);
    }
}
