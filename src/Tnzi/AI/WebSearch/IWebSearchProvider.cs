namespace Tnzi.AI.WebSearch;

/// <summary>
/// Web 搜索提供者抽象 — 可插拔的联网搜索能力
/// </summary>
/// <remarks>
/// <para>默认实现由 Tnzi.AI 模块提供（DuckDuckGo HTML 抓取）。</para>
/// <para>生产环境建议替换为商业搜索 API（Bing Search API, Google Custom Search, Tavily 等）。</para>
/// </remarks>
public interface IWebSearchProvider
{
    /// <summary>
    /// 执行 Web 搜索
    /// </summary>
    /// <param name="query">搜索关键词</param>
    /// <param name="maxResults">最大结果数（1-10）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>搜索结果列表</returns>
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int maxResults = 5, CancellationToken ct = default);
}

/// <summary>
/// Web 搜索结果
/// </summary>
public class WebSearchResult
{
    /// <summary>
    /// 结果标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 结果 URL
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 摘要/片段
    /// </summary>
    public string Snippet { get; set; } = string.Empty;
}
