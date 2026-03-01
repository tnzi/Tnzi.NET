namespace Tnzi.AI.Coder.WebSearch;

/// <summary>
/// DuckDuckGo Web 搜索提供者 — 通过 HTML 抓取实现，适用于开发/测试场景
/// </summary>
/// <remarks>
/// <para>此实现通过抓取 DuckDuckGo HTML 页面获取搜索结果，不需要 API Key。</para>
/// <para>
/// 限制：依赖 DuckDuckGo 页面 HTML 结构，结构变更可能导致解析失败。
/// 生产环境建议使用商业搜索 API（Bing Search API, Tavily, SerpAPI 等）。
/// </para>
/// </remarks>
public class DuckDuckGoSearchProvider : IWebSearchProvider
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DuckDuckGoSearchProvider> _logger;

    public DuckDuckGoSearchProvider(IHttpClientFactory httpClientFactory, ILogger<DuckDuckGoSearchProvider> logger)
    {
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int maxResults = 5, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(query);

        var limit = Math.Clamp(maxResults, 1, 10);
        var encodedQuery = Uri.EscapeDataString(query);
        var searchUrl = $"https://html.duckduckgo.com/html/?q={encodedQuery}";

        _logger.LogDebug("DuckDuckGo search: {Query} (max: {MaxResults})", query, limit);

        using var httpClient = _httpClientFactory.CreateClient("Tnzi.AI.Coder.DuckDuckGo");

        var html = await httpClient.GetStringAsync(searchUrl, ct);
        var results = ParseResults(html, limit);

        _logger.LogDebug("DuckDuckGo search '{Query}' returned {Count} results", query, results.Count);

        return results;
    }

    /// <summary>
    /// 解析 DuckDuckGo HTML 搜索结果
    /// </summary>
    private static List<WebSearchResult> ParseResults(string html, int maxResults)
    {
        var results = new List<WebSearchResult>();

        var resultPattern = new Regex(
            @"<a\s+[^>]*class=""result__a""[^>]*href=""([^""]+)""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);

        var snippetPattern = new Regex(
            @"<a\s+[^>]*class=""result__snippet""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);

        var resultMatches = resultPattern.Matches(html);
        var snippetMatches = snippetPattern.Matches(html);

        for (var i = 0; i < resultMatches.Count && results.Count < maxResults; i++)
        {
            var resultMatch = resultMatches[i];
            var rawUrl = resultMatch.Groups[1].Value;
            var rawTitle = resultMatch.Groups[2].Value;

            var actualUrl = ExtractActualUrl(rawUrl);
            var title = StripHtmlTags(rawTitle).Trim();

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(actualUrl))
            {
                continue;
            }

            var snippet = "";
            if (i < snippetMatches.Count)
            {
                snippet = StripHtmlTags(snippetMatches[i].Groups[1].Value).Trim();
            }

            results.Add(new WebSearchResult
            {
                Title = title,
                Url = actualUrl,
                Snippet = snippet
            });
        }

        return results;
    }

    /// <summary>
    /// 提取 DuckDuckGo 重定向链接中的实际 URL
    /// </summary>
    private static string ExtractActualUrl(string ddgUrl)
    {
        if (ddgUrl.Contains("uddg="))
        {
            var uddgMatch = Regex.Match(ddgUrl, @"uddg=([^&]+)", RegexOptions.None, RegexTimeout);
            if (uddgMatch.Success)
            {
                return Uri.UnescapeDataString(uddgMatch.Groups[1].Value);
            }
        }

        if (ddgUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return ddgUrl;
        }

        if (ddgUrl.StartsWith("//"))
        {
            return "https:" + ddgUrl;
        }

        return ddgUrl;
    }

    /// <summary>
    /// 移除 HTML 标签
    /// </summary>
    private static string StripHtmlTags(string html)
    {
        var cleaned = Regex.Replace(html, @"<[^>]+>", "", RegexOptions.None, RegexTimeout);
        return System.Net.WebUtility.HtmlDecode(cleaned).Trim();
    }
}
