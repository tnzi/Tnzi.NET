namespace Tnzi.AI.Rag.Search;

/// <summary>
/// Deduplication post-processor — removes near-duplicate search results.
/// <para>
/// Uses content similarity (Jaccard similarity on word sets) to detect duplicates.
/// When two results are similar above the threshold, the one with the lower score is removed.
/// </para>
/// </summary>
public class DeduplicationPostProcessor : ISearchPostProcessor
{
    private readonly double _similarityThreshold;
    private readonly ILogger<DeduplicationPostProcessor> _logger;

    /// <summary>
    /// Run early in the pipeline (before enrichment)
    /// </summary>
    public int Order => 10;

    /// <param name="logger">Logger</param>
    /// <param name="similarityThreshold">
    /// Jaccard similarity threshold (0-1). Results with similarity above this are considered duplicates.
    /// Default: 0.85
    /// </param>
    public DeduplicationPostProcessor(ILogger<DeduplicationPostProcessor> logger, double similarityThreshold = 0.85)
    {
        _logger = Check.NotNull(logger);
        _similarityThreshold = similarityThreshold;
    }

    public Task<List<VectorSearchResult>> ProcessAsync(List<VectorSearchResult> results, string query, CancellationToken ct = default)
    {
        if (results.Count <= 1)
        {
            return Task.FromResult(results);
        }

        var deduplicated = new List<VectorSearchResult>();
        var removed = 0;

        foreach (var result in results)
        {
            var isDuplicate = false;

            foreach (var existing in deduplicated)
            {
                if (ComputeJaccardSimilarity(result.Content, existing.Content) >= _similarityThreshold)
                {
                    isDuplicate = true;
                    removed++;
                    break;
                }
            }

            if (!isDuplicate)
            {
                deduplicated.Add(result);
            }
        }

        if (removed > 0)
        {
            _logger.LogDebug("Deduplication removed {Removed} near-duplicate results (threshold={Threshold})",
                removed, _similarityThreshold);
        }

        return Task.FromResult(deduplicated);
    }

    /// <summary>
    /// Compute Jaccard similarity between two text strings based on word sets
    /// </summary>
    private static double ComputeJaccardSimilarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return 0.0;
        }

        var setA = Tokenize(a);
        var setB = Tokenize(b);

        if (setA.Count == 0 || setB.Count == 0)
        {
            return 0.0;
        }

        var intersectionCount = 0;
        foreach (var word in setA)
        {
            if (setB.Contains(word))
            {
                intersectionCount++;
            }
        }

        var unionCount = setA.Count + setB.Count - intersectionCount;
        return unionCount > 0 ? (double)intersectionCount / unionCount : 0.0;
    }

    private static HashSet<string> Tokenize(string text)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var start = -1;

        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && char.IsLetterOrDigit(text[i]))
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                words.Add(text[start..i]);
                start = -1;
            }
        }

        return words;
    }
}
