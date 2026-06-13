namespace Tnzi.AI.Tools.BuiltIn;

/// <summary>
/// Built-in web search tool — delegates to IWebSearchProvider for AI agents
/// </summary>
/// <remarks>
/// Default implementation is DuckDuckGoSearchProvider (registered by AIModule via TryAdd).
/// Register a custom IWebSearchProvider for commercial search APIs.
/// </remarks>
[AIToolGroup("websearch", "Web Search", "Search the web for up-to-date information")]
public class WebSearchTools : IAIToolProvider
{
    private readonly IWebSearchProvider? _searchProvider;
    private readonly ILogger<WebSearchTools> _logger;

    public WebSearchTools(ILogger<WebSearchTools> logger, IWebSearchProvider? searchProvider = null)
    {
        _logger = Check.NotNull(logger);
        _searchProvider = searchProvider;
    }

    /// <summary>
    /// Search the web
    /// </summary>
    [AIFunction("web_search", "Search the web and return results with title, URL, and snippet",
        IsReadOnly = true, IsConcurrencySafe = true, SearchHint = "search web internet")]
    public async Task<object> SearchAsync(
        [AIParameter("query", "The search query")] string query,
        [AIParameter("max_results", "Maximum number of results to return (1-10)", false)] int? maxResults = null)
    {
        if (_searchProvider == null)
        {
            return new { error = "Web search is not available. No IWebSearchProvider is registered." };
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return new { error = "Search query cannot be empty" };
        }

        try
        {
            var limit = Math.Clamp(maxResults ?? 5, 1, 10);

            _logger.LogDebug("Web search: {Query} (max: {MaxResults})", query, limit);

            var results = await _searchProvider.SearchAsync(query, limit);

            return new
            {
                results = results.Select(r => new { title = r.Title, url = r.Url, snippet = r.Snippet }),
                count = results.Count,
                query
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Web search failed for '{Query}'", query);
            return new { error = $"Search failed: {ex.Message}" };
        }
    }
}
