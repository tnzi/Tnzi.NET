namespace Tnzi.AI.Rag.Search;

/// <summary>
/// Score normalization post-processor — normalizes search result scores to [0, 1] range.
/// <para>
/// Uses min-max normalization across the result set. If all scores are equal,
/// they are normalized to 1.0. Also filters out results below a minimum score threshold.
/// </para>
/// </summary>
public class ScoreNormalizationPostProcessor : ISearchPostProcessor
{
    private readonly double _minScoreThreshold;
    private readonly ILogger<ScoreNormalizationPostProcessor> _logger;

    /// <summary>
    /// Run after deduplication but before enrichment
    /// </summary>
    public int Order => 20;

    /// <param name="logger">Logger</param>
    /// <param name="minScoreThreshold">
    /// Minimum normalized score threshold (0-1). Results below this are filtered out.
    /// Default: 0.0 (no filtering)
    /// </param>
    public ScoreNormalizationPostProcessor(ILogger<ScoreNormalizationPostProcessor> logger, double minScoreThreshold = 0.0)
    {
        _logger = Check.NotNull(logger);
        _minScoreThreshold = minScoreThreshold;
    }

    public Task<List<VectorSearchResult>> ProcessAsync(List<VectorSearchResult> results, string query, CancellationToken ct = default)
    {
        if (results.Count == 0)
        {
            return Task.FromResult(results);
        }

        var minScore = results.Min(r => r.Score);
        var maxScore = results.Max(r => r.Score);
        var range = maxScore - minScore;

        // Normalize scores to [0, 1] — 创建新实例，不修改入参对象
        // （immutable pattern，与 WeightedDiminishingReranker 一致）
        var normalized = results
            .Select(r => CloneWithScore(r, range > 0
                ? (r.Score - minScore) / range
                : 1.0)) // All scores equal, normalize to 1.0
            .ToList();

        // Filter by minimum score threshold
        var filtered = normalized;
        if (_minScoreThreshold > 0)
        {
            filtered = normalized.Where(r => r.Score >= _minScoreThreshold).ToList();

            if (filtered.Count < normalized.Count)
            {
                _logger.LogDebug("Score normalization filtered {Removed} results below threshold {Threshold}",
                    normalized.Count - filtered.Count, _minScoreThreshold);
            }
        }

        return Task.FromResult(filtered);
    }

    /// <summary>
    /// 复制结果并替换得分（保持入参不可变）
    /// </summary>
    private static VectorSearchResult CloneWithScore(VectorSearchResult source, double score) => new()
    {
        Id = source.Id,
        Content = source.Content,
        DocumentId = source.DocumentId,
        KnowledgeBaseId = source.KnowledgeBaseId,
        ChunkIndex = source.ChunkIndex,
        Metadata = source.Metadata,
        ParentChunkId = source.ParentChunkId,
        ContentHash = source.ContentHash,
        Score = score
    };
}
