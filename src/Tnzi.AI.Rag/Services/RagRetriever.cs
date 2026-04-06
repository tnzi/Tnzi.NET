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
        IEnumerable<ISearchPostProcessor> postProcessors,
        IOptions<AIRagOptions> ragOptions,
        IQueryRewriter? queryRewriter = null,
        IRelevanceGrader? relevanceGrader = null,
        IGraphSearchService? graphSearchService = null,
        IParentDocumentRetriever? parentDocumentRetriever = null) : base(serviceProvider)
    {
        _embeddingService = Check.NotNull(embeddingService);
        _vectorStore = Check.NotNull(vectorStore);
        _reranker = Check.NotNull(reranker);
        _docRepository = Check.NotNull(docRepository);
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

            // 2. 生成查询向量
            var embeddingResult = await _embeddingService.GenerateEmbeddingAsync(searchQuery, ct: ct);
            if (!embeddingResult.Succeeded)
            {
                Logger.LogWarning("Embedding generation failed for RAG retrieval: {Message}", embeddingResult.Message);
                return [];
            }

            // 3. 向量搜索（支持多知识库）+ 图谱搜索并行
            var vectorTask = SearchVectorAsync(embeddingResult.Data!, options, ct);
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
    /// 执行向量搜索（支持多知识库并行）
    /// </summary>
    private async Task<List<VectorSearchResult>> SearchVectorAsync(
        float[] embedding, RagRetrievalOptions options, CancellationToken ct)
    {
        var allResults = new List<VectorSearchResult>();

        if (options.KnowledgeBaseIds is { Count: > 0 })
        {
            // 并行搜索多个指定知识库
            var tasks = options.KnowledgeBaseIds.Select(kbId =>
                _vectorStore.SearchAsync(embedding, options.TopK, kbId, ct));
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
            allResults = await _vectorStore.SearchAsync(embedding, options.TopK, ct: ct);
        }

        return allResults;
    }

    /// <summary>
    /// 判断是否启用 Parent Document Retrieval（请求级 > 全局配置）
    /// </summary>
    private bool IsParentRetrievalEnabled(RagRetrievalOptions options)
    {
        return options.EnableParentRetrieval ?? _ragOptions.ParentDocumentRetrieval.Enabled;
    }

    /// <summary>
    /// 执行图谱搜索（在指定知识库中搜索匹配的实体及关系）
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
