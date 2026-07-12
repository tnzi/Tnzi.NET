namespace Tnzi.AI.Rag.Services;

/// <summary>
/// RAG 检索器 — 封装查询改写 → 嵌入生成 → 向量搜索 → 图谱搜索 → 后处理的共享检索管线
/// <para>
/// 被 RagQueryEngine（单轮）和 RagChatEngine（多轮）共享使用，
/// 避免两个引擎各自重复实现检索逻辑。
/// </para>
/// <para>
/// 当 <see cref="IGraphSearchService"/> 可用时，图谱搜索与向量搜索并行执行，
/// 图谱搜索结果作为附加上下文片段追加到向量检索结果之后（不参与向量结果的排序）。
/// </para>
/// </summary>
public class RagRetriever : ApplicationService, IRagRetriever
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IReranker _reranker;
    private readonly IRepository<KnowledgeDocument, Guid> _docRepository;
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
        IRepository<KnowledgeDocument, Guid> docRepository,
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
        _docRepository = Check.NotNull(docRepository);
        _kbRepository = Check.NotNull(kbRepository);
        Check.NotNull(postProcessors);
        _sortedProcessors = postProcessors.OrderBy(p => p.Order).ToList();
        _ragOptions = Check.NotNull(ragOptions).Value;
        _queryRewriter = queryRewriter;
        _relevanceGrader = relevanceGrader;
        _graphSearchService = graphSearchService;
        _parentDocumentRetriever = parentDocumentRetriever;
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

            // 2+3. 向量搜索（含 per-KB 对齐的查询向量生成，支持多知识库）+ 图谱搜索并行
            var vectorTask = SearchVectorAsync(searchQuery, options, ct);
            var graphTask = SearchGraphAsync(searchQuery, options, ct);

            await Task.WhenAll(vectorTask, graphTask);

            var allResults = await vectorTask;
            var graphResults = await graphTask;

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
            if (_parentDocumentRetriever != null && IsParentRetrievalEnabled(options))
            {
                var parentOptions = new ParentRetrievalOptions
                {
                    WindowSize = _ragOptions.ParentDocumentRetrieval.WindowSize,
                    MaxTokens = _ragOptions.ParentDocumentRetrieval.MaxTokens
                };
                var parentResults = await _parentDocumentRetriever.RetrieveAsync(results, parentOptions, ct);

                if (parentResults.Count > 0)
                {
                    // 替换原始结果为扩展后的上下文块
                    results = parentResults.Select(pr => new RetrievalResult
                    {
                        Content = pr.MergedContent,
                        Score = pr.Score,
                        DocumentId = pr.DocumentId,
                        Metadata = new Dictionary<string, object>
                        {
                            ["searchType"] = "parent_document",
                            ["startChunkIndex"] = pr.StartChunkIndex,
                            ["endChunkIndex"] = pr.EndChunkIndex,
                            ["documentName"] = pr.DocumentName ?? string.Empty
                        }
                    }).ToList();

                    Logger.LogDebug("Parent document retrieval expanded results to {Count} context blocks", results.Count);
                }
            }

            // 10. 追加图谱搜索上下文片段（不参与向量结果排序，作为补充上下文）
            if (graphResults.Count > 0)
            {
                foreach (var graphResult in graphResults)
                {
                    results.Add(new RetrievalResult
                    {
                        Content = graphResult.ContextSnippet,
                        Score = graphResult.Score,
                        Metadata = new Dictionary<string, object>
                        {
                            ["searchType"] = "graph",
                            ["nodeName"] = graphResult.NodeName,
                            ["nodeType"] = graphResult.NodeType
                        }
                    });
                }

                Logger.LogDebug("Appended {Count} graph context snippets to retrieval results", graphResults.Count);
            }

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

    /// <summary>
    /// 判断是否启用 Parent Document Retrieval（请求级 > 全局配置）
    /// </summary>
    private bool IsParentRetrievalEnabled(RagRetrievalOptions options)
    {
        return options.EnableParentRetrieval ?? _ragOptions.ParentDocumentRetrieval.Enabled;
    }

    /// <summary>
    /// 执行图谱搜索（在指定知识库中搜索匹配的实体及关系）。
    /// <para>
    /// <b>有意限定为 KB-scoped</b>：<see cref="IGraphSearchService.SearchAsync"/> 的契约要求传入具体的
    /// <c>knowledgeBaseId</c>，不存在跨库 / search-all 的重载。因此当请求未指定 <c>KnowledgeBaseIds</c>
    /// （search-all 路径）时跳过图谱搜索，仅返回向量结果。要让图谱参与检索，调用方需显式指定一个或多个
    /// 知识库 ID（图谱搜索内部经 EF 全局过滤器自动应用租户隔离，与向量 raw-SQL 路径不同）。
    /// 跨库图谱搜索属于刻意推迟的范围扩展，不在 D1 默认关闭的接线内。
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<GraphSearchResult>> SearchGraphAsync(
        string query, RagRetrievalOptions options, CancellationToken ct)
    {
        if (_graphSearchService == null || options.KnowledgeBaseIds is not { Count: > 0 })
        {
            return [];
        }

        try
        {
            var graphOptions = new GraphSearchOptions(MaxResults: 3);

            var tasks = options.KnowledgeBaseIds.Select(kbId =>
                _graphSearchService.SearchAsync(query, kbId, graphOptions, ct));
            var resultSets = await Task.WhenAll(tasks);

            return resultSets
                .SelectMany(r => r)
                .OrderByDescending(r => r.Score)
                .Take(graphOptions.MaxResults)
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Graph search failed, continuing with vector results only");
            return [];
        }
    }
}
