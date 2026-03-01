namespace Tnzi.AI.Tools.Examples;

/// <summary>
/// Web 搜索工具 — 通过 IWebSearchProvider 为 AI Agent 提供联网搜索能力
/// </summary>
/// <remarks>
/// 默认实现由 Tnzi.AI.Coder 模块提供（DuckDuckGo）。
/// 用户可注册自定义 IWebSearchProvider 使用商业搜索 API。
/// </remarks>
[AIToolGroup("websearch", "Web Search", "Search the web for information")]
public class WebSearchTools : IAIToolProvider
{
    private readonly IWebSearchProvider? _searchProvider;
    private readonly ILogger<WebSearchTools> _logger;

    public WebSearchTools(IWebSearchProvider? searchProvider, ILogger<WebSearchTools> logger)
    {
        _searchProvider = searchProvider;
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 搜索 Web
    /// </summary>
    [AIFunction("web_search", "Search the web and return results with title, URL and snippet")]
    public async Task<object> SearchAsync(
        [AIParameter("query", "Search query")] string query,
        [AIParameter("max_results", "Maximum number of results (1-10)", false)] int? maxResults = null)
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
