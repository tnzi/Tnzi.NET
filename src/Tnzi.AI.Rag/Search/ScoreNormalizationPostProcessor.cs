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

        // Normalize scores to [0, 1]
        foreach (var result in results)
        {
            result.Score = range > 0
                ? (result.Score - minScore) / range
                : 1.0; // All scores equal, normalize to 1.0
        }

        // Filter by minimum score threshold
        var filtered = results;
        if (_minScoreThreshold > 0)
        {
            var originalCount = results.Count;
            filtered = results.Where(r => r.Score >= _minScoreThreshold).ToList();

            if (filtered.Count < originalCount)
            {
                _logger.LogDebug("Score normalization filtered {Removed} results below threshold {Threshold}",
                    originalCount - filtered.Count, _minScoreThreshold);
            }
        }

        return Task.FromResult(filtered);
    }
}
