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
/// <para>
/// 按库检索只查存在且<b>启用中</b>的知识库，向量与图谱都是（见 <see cref="RetrievalAugmentation.LoadSearchableKnowledgeBasesAsync"/>）。
/// </para>
/// <para>
/// <b>相关度是绝对值</b>：<see cref="RagRetrievalOptions.MinRelevance"/> 比的、结果里的 <see cref="RetrievalResult.Score"/> 报的，
/// 都是检索（经 <see cref="IReranker"/>）给出的相关度，默认重排器原样透传，即余弦相似度。后处理管线（去重、min-max 归一化、
/// 加权递减）只决定取舍与先后。★ 此前阈值作用在归一化之后：最高一条恒为 1.0、最低一条恒为 0，默认阈值 0.3 下五条都高度相关的结果
/// 丢掉末尾两条，只命中一条毫不相关的分块时它被归一成 1.0、照样通过，还以 <c>relevance: 1.00</c> 写进 prompt 与引用。
/// </para>
/// <para>
/// 每次检索记一次 <c>rag.search</c> Activity 与 <c>rag.search.count</c> / <c>rag.search.duration</c>（只按一个库检索时带上库 Id），
/// 失败记 <c>rag.error.count</c>（<c>rag.operation=retrieval</c>）。这里是用户端两个引擎与默认 agent 检索
/// （<c>VectorTextSearchService</c>）共同走的路径；混合检索不经过这里、自己记，所以每次请求只记一次。
/// ★ 此前只有管理端的检索测试与混合检索记指标：默认配置下 agent 与用户的全部检索流量在仪表盘上看不见。
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

        var singleKnowledgeBase = options.KnowledgeBaseIds is { Count: > 0 } ids && ids.Distinct().Count() == 1
            ? ids[0]
            : (Guid?)null;
        using var activity = RagActivitySource.StartSearchActivity(options.TopK, singleKnowledgeBase);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var results = await RetrieveCoreAsync(query, options, ct);
            RagActivitySource.RecordSearch(results.Count, stopwatch.Elapsed.TotalSeconds, singleKnowledgeBase);
            return results;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 调用方取消（客户端断开）不是检索故障：不记错误指标，也不记 Error 日志
            return [];
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "RAG retrieval failed for query length {Length}", query.Length);
            RagActivitySource.RecordError("retrieval", ex);
            RagActivitySource.RecordActivityError(activity, ex);
            return [];
        }
    }

    /// <summary>
    /// 检索管线本身（知识库范围 → 查询改写 → 向量与图谱 → 重排 → 阈值 → 后处理 → 评分 → 父文档 → 图谱片段）。
    /// </summary>
    private async Task<List<RetrievalResult>> RetrieveCoreAsync(string query, RagRetrievalOptions options, CancellationToken ct)
    {
        // 1. 知识库范围：按库检索只查存在且启用中的库；一个都不剩就什么都不查（连查询改写与嵌入都省掉）。
        //    search-all（不指定库）由向量库自己的 SQL 排除禁用库。
        List<KnowledgeBase>? knowledgeBases = null;
        if (options.KnowledgeBaseIds is { Count: > 0 })
        {
            knowledgeBases = await RetrievalAugmentation.LoadSearchableKnowledgeBasesAsync(
                _kbRepository, options.KnowledgeBaseIds.Distinct().ToList(), Logger, ct);
            if (knowledgeBases.Count == 0)
            {
                return [];
            }
        }

        // 2. 查询改写（可选）
        var searchQuery = query;
        if (_queryRewriter != null)
        {
            var rewritten = await _queryRewriter.RewriteAsync(query, ct);
            if (!string.IsNullOrWhiteSpace(rewritten))
            {
                searchQuery = rewritten;
            }
        }

        // 3+4. 向量搜索（含 per-KB 对齐的查询向量生成，支持多知识库）→ 图谱搜索。
        // 必须顺序执行：两者都经本 scope 的 RagDbContext 查库（图谱路径读 KnowledgeGraphNode/Edge），
        // 并发使用同一 DbContext 会触发 "A second operation was started on this context" 并被下方 catch 吞成空结果。
        var allResults = await SearchVectorAsync(searchQuery, knowledgeBases, options.TopK, ct);
        var graphResults = await RetrievalAugmentation.SearchGraphAsync(
            _graphSearchService, searchQuery, knowledgeBases?.Select(kb => kb.Id).ToList(), Logger, ct);

        // 5. 重排序
        allResults = await _reranker.RerankAsync(query, allResults, options.TopK, ct);

        // 6. 按最低相关性过滤：比的是检索 / 重排给出的绝对相关度，必须在归一化之前（见类注释）
        if (options.MinRelevance > 0)
        {
            allResults = allResults.Where(r => r.Score >= options.MinRelevance).ToList();
        }

        var relevanceByChunk = new Dictionary<Guid, double>();
        foreach (var result in allResults)
        {
            relevanceByChunk.TryAdd(result.Id, result.Score);
        }

        // 7. 运行后处理管线（去重、归一化、加权递减）：只决定取舍与先后
        foreach (var processor in _sortedProcessors)
        {
            allResults = await processor.ProcessAsync(allResults, query, ct);
        }

        // 8. 转换为 RetrievalResult：分数报回绝对相关度（自定义后处理器造出的新块没有原值，沿用它给的分数）；
        //    始终包含 chunkIndex 以支持 Parent Document Retrieval
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
                Score = relevanceByChunk.TryGetValue(r.Id, out var relevance) ? relevance : r.Score,
                KnowledgeBaseId = r.KnowledgeBaseId,
                DocumentId = r.DocumentId,
                Metadata = metadata
            };
        }).ToList();

        // 9. 相关性评分（可选）
        if (_relevanceGrader != null && results.Count > 0)
        {
            var textResults = results.Select(r => new TextSearchResult
            {
                Text = r.Content,
                Score = r.Score
            }).ToList();

            var graded = await _relevanceGrader.GradeAsync(query, textResults, ct);
            var relevantTexts = graded.Where(g => g.IsRelevant).Select(g => g.Result.Text).ToHashSet();
            results = results.Where(r => relevantTexts.Contains(r.Content)).ToList();
        }

        // 10. Parent Document Retrieval（可选：将细粒度匹配块扩展为更大的上下文窗口）
        results = await RetrievalAugmentation.ExpandParentsAsync(
            _parentDocumentRetriever, results, _ragOptions, options.EnableParentRetrieval, Logger, ct);

        // 11. 追加图谱搜索上下文片段（不参与向量结果排序，作为补充上下文）
        results = RetrievalAugmentation.AppendGraphSnippets(results, graphResults);

        Logger.LogDebug("RAG retrieval returned {Count} results for query length {Length}",
            results.Count, query.Length);

        return results;
    }

    /// <summary>
    /// 从检索结果构建引用 DTO 列表（共享方法，供 RagQueryEngine 和 RagChatEngine 使用）
    /// </summary>
    public static List<CitationDto> BuildCitations(List<RetrievalResult> results, int maxContentLength = 200)
    {
        return results.Select(r => new CitationDto
        {
            Text = r.Content.Truncate(maxContentLength),
            Score = r.Score
        }).ToList();
    }

    /// <summary>
    /// 执行向量搜索（含查询向量生成，支持多知识库并行）。
    /// <para>
    /// query 向量必须与各知识库摄取时使用相同的 embedding provider/model，否则向量空间不一致、
    /// 检索结果无意义（与 <c>KnowledgeBaseService.SearchAsync</c> 同范式）。多 KB 且嵌入配置不一致时，
    /// 按 (provider, model) 分组逐组生成 query 向量后分别检索；未指定 KB（<paramref name="knowledgeBases"/> 为 null，search-all）时使用全局默认配置。
    /// </para>
    /// </summary>
    private async Task<List<VectorSearchResult>> SearchVectorAsync(
        string searchQuery, IReadOnlyList<KnowledgeBase>? knowledgeBases, int topK, CancellationToken ct)
    {
        if (knowledgeBases is not null)
        {
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
                    _vectorStore.SearchAsync(embeddingResult.Data!, topK, kbId, ct));
                var resultSets = await Task.WhenAll(tasks);
                foreach (var resultSet in resultSets)
                {
                    allResults.AddRange(resultSet);
                }
            }

            // 跨知识库结果按得分排序并截取 TopK
            return allResults
                .OrderByDescending(r => r.Score)
                .Take(topK)
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

        return await _vectorStore.SearchAsync(defaultEmbedding.Data!, topK, ct: ct);
    }
}
