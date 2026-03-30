namespace Tnzi.AI.Rag.Search;

/// <summary>
/// 加权递减重排序后处理器 — 灵感来自 Kernel Memory v2
/// <para>
/// 三阶段处理：
/// Phase 1: 权重应用 — weighted_score = base_relevance × source_weight
/// Phase 2: 递减聚合 — 按 ContentHash 分组，相同内容多次出现时累加 score × diminishingFactor^i
/// Phase 3: 排序 — 按得分降序，同分则按 ChunkIndex 升序
/// </para>
/// </summary>
public class WeightedDiminishingReranker : ISearchPostProcessor
{
    private readonly IOptions<AIRagOptions> _ragOptions;
    private readonly ILogger<WeightedDiminishingReranker> _logger;

    /// <summary>
    /// Run after deduplication(10) and normalization(20), before other enrichment
    /// </summary>
    public int Order => 50;

    public WeightedDiminishingReranker(IOptions<AIRagOptions> ragOptions, ILogger<WeightedDiminishingReranker> logger)
    {
        _ragOptions = Check.NotNull(ragOptions);
        _logger = Check.NotNull(logger);
    }

    public Task<List<VectorSearchResult>> ProcessAsync(List<VectorSearchResult> results, string query, CancellationToken ct = default)
    {
        if (results.Count <= 1)
        {
            return Task.FromResult(results);
        }

        var options = _ragOptions.Value.Reranker;

        // Phase 1: 权重应用
        var weightedResults = ApplyWeights(results, options);

        // Phase 2: 递减聚合（按 ContentHash 分组）
        var aggregated = DiminishingAggregate(weightedResults, options);

        // Phase 3: 排序 — 按得分降序，同分按 ChunkIndex 升序
        var sorted = aggregated
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.ChunkIndex)
            .ToList();

        _logger.LogDebug(
            "WeightedDiminishingReranker: {InputCount} → {OutputCount} results (diminishing={Factor}, maxScore={MaxScore})",
            results.Count, sorted.Count, options.DiminishingFactor, options.MaxMergedScore);

        return Task.FromResult(sorted);
    }

    /// <summary>
    /// Phase 1: 按知识库权重调整每个结果的得分
    /// </summary>
    private static List<VectorSearchResult> ApplyWeights(List<VectorSearchResult> results, RerankerOptions options)
    {
        if (options.KnowledgeBaseWeights.Count == 0)
        {
            return results;
        }

        // 创建新列表，不修改原始对象（immutable pattern）
        return results.Select(r =>
        {
            var kbId = r.KnowledgeBaseId.ToString();
            var weight = options.KnowledgeBaseWeights.GetValueOrDefault(kbId, 1.0);

            return new VectorSearchResult
            {
                Id = r.Id,
                Content = r.Content,
                DocumentId = r.DocumentId,
                KnowledgeBaseId = r.KnowledgeBaseId,
                ChunkIndex = r.ChunkIndex,
                Metadata = r.Metadata,
                ParentChunkId = r.ParentChunkId,
                ContentHash = r.ContentHash,
                Score = r.Score * weight
            };
        }).ToList();
    }

    /// <summary>
    /// Phase 2: 按内容分组，递减聚合得分
    /// <para>
    /// 同一内容出现在多个搜索结果中时，
    /// 按得分降序排列，第 i 次的贡献 = score × diminishingFactor^i，
    /// 合并后得分上限为 MaxMergedScore。
    /// 使用 ContentHash 分组；若 ContentHash 为空，则从 Content 文本计算 hash。
    /// </para>
    /// </summary>
    private static List<VectorSearchResult> DiminishingAggregate(List<VectorSearchResult> results, RerankerOptions options)
    {
        // 计算每个结果的有效 hash（优先 ContentHash，否则从 Content 计算）
        var hashKeySelector = (VectorSearchResult r) =>
            !string.IsNullOrWhiteSpace(r.ContentHash)
                ? r.ContentHash
                : HashHelper.GetSha256(r.Content);

        // 按 hash 分组
        var groups = results.GroupBy(hashKeySelector);
        var aggregated = new List<VectorSearchResult>();

        foreach (var group in groups)
        {
            // 按得分降序排列组内结果
            var sorted = group.OrderByDescending(r => r.Score).ToList();
            var bestResult = sorted[0];

            // 递减累加得分
            var mergedScore = 0.0;
            for (var i = 0; i < sorted.Count; i++)
            {
                mergedScore += sorted[i].Score * Math.Pow(options.DiminishingFactor, i);
            }

            // 上限限制
            mergedScore = Math.Min(mergedScore, options.MaxMergedScore);

            // 使用最高得分结果作为代表，更新合并后的得分
            aggregated.Add(new VectorSearchResult
            {
                Id = bestResult.Id,
                Content = bestResult.Content,
                DocumentId = bestResult.DocumentId,
                KnowledgeBaseId = bestResult.KnowledgeBaseId,
                ChunkIndex = bestResult.ChunkIndex,
                Metadata = bestResult.Metadata,
                ParentChunkId = bestResult.ParentChunkId,
                ContentHash = bestResult.ContentHash,
                Score = mergedScore
            });
        }

        return aggregated;
    }

}
