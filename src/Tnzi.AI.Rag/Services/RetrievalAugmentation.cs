namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 检索结果的两段增强 —— 图谱上下文片段（GraphRAG）与父文档窗口扩展（Parent Document Retrieval）——
/// 以及 <see cref="RetrievalResult"/> → <see cref="TextSearchResult"/> 的映射。
/// </summary>
/// <remarks>
/// ★ 这两段此前只长在 <see cref="RagRetriever"/> 里，而 <c>IRagRetriever</c> 的消费者只有用户直连的
/// <c>/api/rag/query|chat</c> 两个引擎；agent 对话走 <c>ITextSearchService</c>（<see cref="VectorTextSearchService"/> /
/// <c>HybridSearchService</c>），一段都没有。于是运营方打开 <c>AI:Rag:GraphRag:Enabled</c>（每份文档多付一次 LLM 抽取）
/// 与 <c>ParentDocumentRetrieval.Enabled</c>，主路径的 agent 回答里从来没出现过图谱片段或父窗口。
/// 抽成一处，三条检索路径共用同一份实现。
/// </remarks>
internal static class RetrievalAugmentation
{
    /// <summary>图谱搜索每次调用附加的最大片段数。</summary>
    private const int GraphMaxResults = 3;

    /// <summary>
    /// 把 agent 侧传来的 <see cref="TextSearchFilter"/> 收敛成本次检索的知识库范围；返回 <see langword="null"/> 表示本次检索必须答空。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>没有知识库范围就不检索</b>。agent 侧的知识库范围只来自 <c>AgentKnowledgeGrant</c>：无 agent 的对话、
    /// 没有知识库授权的 agent，<c>TextSearchProvider</c> 传来的都是 null。此前两条实现把它当 search-all，
    /// 于是任何已登录用户不带 agentId 发一句话，就拿到全部启用知识库的原文分块（含刻意不对普通用户开放的库），
    /// 还随 citations 原样返回，与用户直连端点「普通用户永远拿不到 search-all」的授权正面冲突。
    /// </para>
    /// <para>
    /// ★ <b>表达不了的过滤维度不能忽略</b>。知识库分块没有用户、agent、线程、集合或元数据维度：调用方要的是
    /// 「这个用户 / 这个线程的东西」（<c>ChatHistoryMemoryProvider</c> 正是这样过滤的），忽略这些条件就等于把范围
    /// 静默放宽成整个知识库。答空是如实的：本实现不存聊天历史。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Guid>? ResolveKnowledgeBaseScope(TextSearchFilter? filter)
    {
        if (filter?.KnowledgeBaseIds is not { Count: > 0 } knowledgeBaseIds)
        {
            return null;
        }

        var hasUnsupportedDimension =
            filter.UserId.HasValue
            || filter.AgentId.HasValue
            || !string.IsNullOrEmpty(filter.ThreadId)
            || !string.IsNullOrEmpty(filter.CollectionName)
            || filter.Metadata is { Count: > 0 };

        return hasUnsupportedDimension ? null : knowledgeBaseIds.Distinct().ToList();
    }

    /// <summary>
    /// 加载本次按库检索范围里<b>仍可检索</b>的知识库：存在（EF 全局过滤器已排除软删与他租户的库）且启用中。
    /// 两类被跳过的库分开记日志，运维看得出是「找不到」还是「被禁用」。
    /// </summary>
    /// <remarks>
    /// ★ 禁用开关此前只在 search-all 的 SQL 里生效：按库检索只按 id 加载、不看 <c>IsEnabled</c>，向量库与全文检索的按库 SQL 也只比
    /// <c>KnowledgeBaseId</c>。于是管理员禁用一个内容过期的库后，被授权它的 agent（主路径）照样检索、引用它。
    /// 在服务层过滤，任何 <see cref="IVectorStore"/> / <see cref="IKeywordSearchProvider"/> 实现都同样生效；
    /// 管理端单库调试搜索（<c>KnowledgeBaseService.SearchAsync</c>）刻意不经这里，仍可检查一个已禁用的库。
    /// </remarks>
    public static async Task<List<KnowledgeBase>> LoadSearchableKnowledgeBasesAsync(
        IRepository<KnowledgeBase, Guid> knowledgeBaseRepository,
        IReadOnlyCollection<Guid> knowledgeBaseIds,
        ILogger logger,
        CancellationToken ct)
    {
        var found = await knowledgeBaseRepository.AsQueryable()
            .Where(kb => knowledgeBaseIds.Contains(kb.Id))
            .ToListAsync(ct);

        if (found.Count < knowledgeBaseIds.Count)
        {
            logger.LogWarning(
                "RAG retrieval skipped {Missing} of {Requested} requested knowledge bases (not found or not accessible)",
                knowledgeBaseIds.Count - found.Count, knowledgeBaseIds.Count);
        }

        var enabled = found.Where(kb => kb.IsEnabled).ToList();
        if (enabled.Count < found.Count)
        {
            logger.LogInformation(
                "RAG retrieval skipped {Disabled} disabled knowledge bases of {Requested} requested",
                found.Count - enabled.Count, knowledgeBaseIds.Count);
        }

        return enabled;
    }

    /// <summary>
    /// 在指定知识库中做图谱搜索。<b>刻意限定为 KB-scoped</b>：<see cref="IGraphSearchService.SearchAsync"/> 的契约要求传入
    /// 具体的 <c>knowledgeBaseId</c>，没有跨库重载；未指定知识库（search-all）时跳过。任何失败都降级为空结果。
    /// </summary>
    public static async Task<IReadOnlyList<GraphSearchResult>> SearchGraphAsync(
        IGraphSearchService? graphSearchService,
        string query,
        IReadOnlyList<Guid>? knowledgeBaseIds,
        ILogger logger,
        CancellationToken ct)
    {
        if (graphSearchService == null || knowledgeBaseIds is not { Count: > 0 })
        {
            return [];
        }

        try
        {
            var graphOptions = new GraphSearchOptions(MaxResults: GraphMaxResults);

            // 逐库顺序检索：GraphSearchService 走 EF 仓储，多 KB 并行会并发使用同一 scoped DbContext。
            var merged = new List<GraphSearchResult>();
            foreach (var kbId in knowledgeBaseIds)
            {
                merged.AddRange(await graphSearchService.SearchAsync(query, kbId, graphOptions, ct));
            }

            return merged
                .OrderByDescending(r => r.Score)
                .Take(graphOptions.MaxResults)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Graph search failed, continuing with vector results only");
            return [];
        }
    }

    /// <summary>
    /// 把细粒度匹配块扩展为父文档窗口。未启用（<paramref name="enabledOverride"/> ?? 全局配置）或无扩展结果时原样返回。
    /// </summary>
    public static async Task<List<RetrievalResult>> ExpandParentsAsync(
        IParentDocumentRetriever? parentDocumentRetriever,
        List<RetrievalResult> results,
        AIRagOptions ragOptions,
        bool? enabledOverride,
        ILogger logger,
        CancellationToken ct)
    {
        var enabled = enabledOverride ?? ragOptions.ParentDocumentRetrieval.Enabled;
        if (parentDocumentRetriever == null || !enabled || results.Count == 0)
        {
            return results;
        }

        var parentOptions = new ParentRetrievalOptions
        {
            WindowSize = ragOptions.ParentDocumentRetrieval.WindowSize,
            MaxTokens = ragOptions.ParentDocumentRetrieval.MaxTokens
        };
        var parentResults = await parentDocumentRetriever.RetrieveAsync(results, parentOptions, ct);
        if (parentResults.Count == 0)
        {
            return results;
        }

        logger.LogDebug("Parent document retrieval expanded results to {Count} context blocks", parentResults.Count);

        return parentResults.Select(pr => new RetrievalResult
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
    }

    /// <summary>把图谱片段追加到结果末尾（不参与向量结果排序，作为补充上下文）。</summary>
    public static List<RetrievalResult> AppendGraphSnippets(List<RetrievalResult> results, IReadOnlyList<GraphSearchResult> graphResults)
    {
        if (graphResults.Count == 0)
        {
            return results;
        }

        return
        [
            .. results,
            .. graphResults.Select(graphResult => new RetrievalResult
            {
                Content = graphResult.ContextSnippet,
                Score = graphResult.Score,
                Metadata = new Dictionary<string, object>
                {
                    ["searchType"] = "graph",
                    ["nodeName"] = graphResult.NodeName,
                    ["nodeType"] = graphResult.NodeType
                }
            })
        ];
    }

    /// <summary>
    /// 映射为 agent 上下文注入用的 <see cref="TextSearchResult"/>：保留检索元数据（chunkIndex / searchType / 图谱节点等），
    /// 补上 documentId / knowledgeBaseId，来源名取文档名（父窗口结果自带 documentName）。
    /// </summary>
    public static List<TextSearchResult> ToTextSearchResults(
        IEnumerable<RetrievalResult> results,
        IReadOnlyDictionary<Guid, string> documentNames)
    {
        return results.Select(r =>
        {
            var metadata = new Dictionary<string, object?>();
            if (r.Metadata != null)
            {
                foreach (var (key, value) in r.Metadata)
                {
                    metadata[key] = value;
                }
            }

            if (r.DocumentId != Guid.Empty) metadata["documentId"] = r.DocumentId;
            if (r.KnowledgeBaseId != Guid.Empty) metadata["knowledgeBaseId"] = r.KnowledgeBaseId;

            var sourceName = documentNames.GetValueOrDefault(r.DocumentId);
            if (sourceName is null && r.Metadata?.GetValueOrDefault("documentName") is string documentName && documentName.Length > 0)
            {
                sourceName = documentName;
            }

            return new TextSearchResult
            {
                Text = r.Content,
                SourceName = sourceName,
                Score = r.Score,
                Metadata = metadata
            };
        }).ToList();
    }
}
