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
