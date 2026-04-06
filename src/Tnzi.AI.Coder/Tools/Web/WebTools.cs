namespace Tnzi.AI.Coder.Web;

/// <summary>
/// Web 访问工具组 — 获取/发送 URL 内容、搜索
/// </summary>
[AIToolGroup("web", "Web Access", "Search the web and fetch URLs")]
public class WebTools : IAIToolProvider
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebSearchProvider _searchProvider;
    private readonly CoderOptions _options;
    private readonly ILogger<WebTools> _logger;

    public WebTools(IHttpClientFactory httpClientFactory, IWebSearchProvider searchProvider,
        IOptions<CoderOptions> options, ILogger<WebTools> logger)
    {
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _searchProvider = Check.NotNull(searchProvider);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 获取 URL 内容
    /// </summary>
    [AIFunction("web_fetch", "Fetch content from a URL", IsReadOnly = true, IsConcurrencySafe = true, SearchHint = "fetch download url web")]
    public async Task<object> WebFetchAsync(
        [AIParameter("url", "The URL to fetch")] string url)
    {
        try
        {
            if (!TryParseHttpUrl(url, out var uri))
            {
                return new { error = $"Invalid URL: {url}. Must be a valid HTTP or HTTPS URL." };
            }

            // SSRF 防护：阻止访问内部/私有地址
            if (await IsBlockedAddressAsync(uri))
            {
                return new { error = "Access to private/internal addresses is not allowed" };
            }

            _logger.LogDebug("Fetching URL: {Url}", url);

            using var httpClient = _httpClientFactory.CreateClient("Tnzi.AI.Coder");

            using var response = await httpClient.GetAsync(uri);

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var rawContent = await response.Content.ReadAsStringAsync();

            var content = ProcessResponseContent(rawContent, contentType, out var truncated);

            _logger.LogDebug("Fetched URL '{Url}': {Length} chars, content-type: {ContentType}",
                url, content.Length, contentType);

            return new
            {
                content,
                url,
                status_code = (int)response.StatusCode,
                content_type = contentType,
                truncated
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "HTTP request failed for '{Url}'", url);
            return new { error = $"HTTP request failed: {ex.Message}" };
        }
        catch (TaskCanceledException)
        {
            return new { error = $"Request timed out for URL: {url}" };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch URL '{Url}'", url);
            return new { error = $"Failed to fetch URL: {ex.Message}" };
        }
    }

    /// <summary>
    /// 发送 HTTP POST 请求
    /// </summary>
    [AIFunction("web_post", "Send an HTTP POST request with a body", SearchHint = "post http request")]
    public async Task<object> WebPostAsync(
        [AIParameter("url", "The URL to send the POST request to")] string url,
        [AIParameter("body", "The request body (JSON string)")] string body,
        [AIParameter("content_type", "Content type header", false)] string? contentType = null,
        [AIParameter("headers", "Additional headers as comma-separated Key:Value pairs", false)] string? headers = null)
    {
        try
        {
            if (!TryParseHttpUrl(url, out var uri))
            {
                return new { error = $"Invalid URL: {url}. Must be a valid HTTP or HTTPS URL." };
            }

            if (await IsBlockedAddressAsync(uri))
            {
                return new { error = "Access to private/internal addresses is not allowed" };
            }

            _logger.LogDebug("POST to URL: {Url}", url);

            using var httpClient = _httpClientFactory.CreateClient("Tnzi.AI.Coder");

            var mediaType = contentType ?? "application/json";
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType)
            };

            var blockedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Host", "Authorization", "Cookie", "Transfer-Encoding",
                "Content-Length", "Content-Type", "Connection"
            };

            // 解析自定义 headers
            if (!string.IsNullOrEmpty(headers))
            {
                foreach (var header in headers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var colonIndex = header.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var key = header[..colonIndex].Trim();
                        if (!blockedHeaders.Contains(key))
                        {
                            requestMessage.Headers.TryAddWithoutValidation(key, header[(colonIndex + 1)..].Trim());
                        }
                    }
                }
            }

            using var response = await httpClient.SendAsync(requestMessage);

            var responseContentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var rawResponse = await response.Content.ReadAsStringAsync();

            var responseBody = ProcessResponseContent(rawResponse, responseContentType, out var truncated);

            _logger.LogDebug("POST to '{Url}' completed with status {StatusCode}", url, (int)response.StatusCode);

            return new
            {
                content = responseBody,
                url,
                status_code = (int)response.StatusCode,
                content_type = responseContentType,
                truncated
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "HTTP POST failed for '{Url}'", url);
            return new { error = $"HTTP request failed: {ex.Message}" };
        }
        catch (TaskCanceledException)
        {
            return new { error = $"Request timed out for URL: {url}" };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to POST to '{Url}'", url);
            return new { error = $"Failed to send POST request: {ex.Message}" };
        }
    }

    /// <summary>
    /// Web 搜索 — 委托给 IWebSearchProvider
    /// </summary>
    [AIFunction("web_search", "Search the web and return results with title, URL and snippet", IsReadOnly = true, IsConcurrencySafe = true, SearchHint = "search web internet")]
    public async Task<object> WebSearchAsync(
        [AIParameter("query", "Search query")] string query,
        [AIParameter("max_results", "Maximum number of results (1-10)", false)] int? maxResults = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new { error = "Search query cannot be empty" };
            }

            var limit = Math.Clamp(maxResults ?? 5, 1, 10);

            _logger.LogDebug("Web search: {Query} (max: {MaxResults})", query, limit);

            var results = await _searchProvider.SearchAsync(query, limit);

            _logger.LogDebug("Web search '{Query}' returned {Count} results", query, results.Count);

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

    /// <summary>
    /// 验证 URL 是否为有效的 HTTP/HTTPS URL
    /// </summary>
    private static bool TryParseHttpUrl(string url, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>
    /// 处理 HTTP 响应内容：HTML 转 Markdown 并截断
    /// </summary>
    private string ProcessResponseContent(string rawContent, string contentType, out bool truncated)
    {
        var maxSize = _options.Sandbox.MaxOutputSize;
        truncated = rawContent.Length > maxSize;

        string content;
        if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            content = ConvertHtmlToMarkdown(rawContent);
        }
        else
        {
            content = rawContent;
        }

        if (content.Length > maxSize)
        {
            content = content[..(int)maxSize] + "\n... (truncated)";
            truncated = true;
        }

        return content;
    }

    /// <summary>
    /// SSRF 防护：DNS 解析并检查是否为私有/内部地址
    /// </summary>
    private static async Task<bool> IsBlockedAddressAsync(Uri uri)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host);

            foreach (var address in addresses)
            {
                if (IsBlockedAddress(address))
                    return true;
            }

            return false;
        }
        catch (SocketException)
        {
            // DNS 解析失败，阻止访问以保安全
            return true;
        }
    }

    /// <summary>
    /// 检查单个 IP 地址是否为被阻止的私有/内部地址
    /// </summary>
    internal static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        // IPv4-mapped IPv6 地址 (::ffff:x.x.x.x) — 提取内嵌的 IPv4 并递归检查
        if (address.IsIPv4MappedToIPv6)
            return IsBlockedAddress(address.MapToIPv4());

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            // 0.0.0.0 — 绑定所有网络接口
            if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0)
                return true;

            // RFC1918: 10.0.0.0/8
            if (bytes[0] == 10)
                return true;

            // RFC1918: 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;

            // RFC1918: 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;

            // Link-local: 169.254.0.0/16
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;

            // Shared address space (CGN): 100.64.0.0/10
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // IPv6 loopback (::1) 已由 IPAddress.IsLoopback 覆盖

            // IPv6 link-local (fe80::/10)
            if (address.IsIPv6LinkLocal)
                return true;

            // IPv6 Unique Local Address (fd00::/8) — 等同于 RFC1918
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 0xfd)
                return true;
        }

        return false;
    }

    /// <summary>
    /// HTML 转 Markdown — 将常见 HTML 标签转换为 Markdown 格式
    /// </summary>
    private static string ConvertHtmlToMarkdown(string html)
    {
        // 移除 script 和 style 标签及其内容
        var result = Regex.Replace(html, @"<(script|style)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase, RegexTimeout);

        // 标题: <h1>-<h6> → # - ######
        result = Regex.Replace(result, @"<h1[^>]*>(.*?)</h1>", "\n# $1\n", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        result = Regex.Replace(result, @"<h2[^>]*>(.*?)</h2>", "\n## $1\n", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        result = Regex.Replace(result, @"<h3[^>]*>(.*?)</h3>", "\n### $1\n", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        result = Regex.Replace(result, @"<h4[^>]*>(.*?)</h4>", "\n#### $1\n", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        result = Regex.Replace(result, @"<h5[^>]*>(.*?)</h5>", "\n##### $1\n", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        result = Regex.Replace(result, @"<h6[^>]*>(.*?)</h6>", "\n###### $1\n", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);

        // 链接: <a href="url">text</a> → [text](url)
        result = Regex.Replace(result, @"<a\s+[^>]*href=""([^""]+)""[^>]*>(.*?)</a>", "[$2]($1)", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);

        // 代码块: <pre><code>...</code></pre> → ```...```
        result = Regex.Replace(result, @"<pre[^>]*>\s*<code[^>]*>([\s\S]*?)</code>\s*</pre>", "\n```\n$1\n```\n", RegexOptions.IgnoreCase, RegexTimeout);

        // 代码块: <pre>...</pre> → ```...```
        result = Regex.Replace(result, @"<pre[^>]*>([\s\S]*?)</pre>", "\n```\n$1\n```\n", RegexOptions.IgnoreCase, RegexTimeout);

        // 行内代码: <code>...</code> → `...`
        result = Regex.Replace(result, @"<code[^>]*>(.*?)</code>", "`$1`", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);

        // 粗体: <strong>/<b> → **...**
        result = Regex.Replace(result, @"<(strong|b)[^>]*>(.*?)</\1>", "**$2**", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);

        // 斜体: <em>/<i> → *...*
        result = Regex.Replace(result, @"<(em|i)[^>]*>(.*?)</\1>", "*$2*", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);

        // 列表项: <li> → -
        result = Regex.Replace(result, @"<li[^>]*>(.*?)</li>", "\n- $1", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);

        // 换行标签: <br>, <p>, <div>, <tr> → 换行
        result = Regex.Replace(result, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase, RegexTimeout);
        result = Regex.Replace(result, @"</(p|div|tr)>", "\n", RegexOptions.IgnoreCase, RegexTimeout);
        result = Regex.Replace(result, @"<(p|div|tr)[^>]*>", "\n", RegexOptions.IgnoreCase, RegexTimeout);

        // 移除剩余 HTML 标签
        result = Regex.Replace(result, @"<[^>]+>", "", RegexOptions.None, RegexTimeout);

        // 解码 HTML 实体
        result = WebUtility.HtmlDecode(result);

        // 合并多余空行
        result = Regex.Replace(result, @"\n{3,}", "\n\n", RegexOptions.None, RegexTimeout);

        return result.Trim();
    }

}
