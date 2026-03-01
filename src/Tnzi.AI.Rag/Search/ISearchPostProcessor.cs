namespace Tnzi.AI.Rag.Search;

/// <summary>
/// Search result post-processor interface — processes search results after retrieval.
/// <para>
/// Post-processors run in sequence and can transform, filter, enrich, or annotate
/// search results. Common use cases include:
/// <list type="bullet">
/// <item>Context expansion (fetching surrounding chunks)</item>
/// <item>Result deduplication (removing near-duplicate chunks)</item>
/// <item>Score normalization</item>
/// <item>Metadata enrichment</item>
/// </list>
/// </para>
/// </summary>
public interface ISearchPostProcessor
{
    /// <summary>
    /// Processing order (lower runs first)
    /// </summary>
    int Order => 0;

    /// <summary>
    /// Process search results
    /// </summary>
    /// <param name="results">Input search results</param>
    /// <param name="query">Original search query</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Processed search results</returns>
    Task<List<VectorSearchResult>> ProcessAsync(List<VectorSearchResult> results, string query, CancellationToken ct = default);
}
