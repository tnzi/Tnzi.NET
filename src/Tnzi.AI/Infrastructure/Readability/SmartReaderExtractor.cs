using SmartReader;

namespace Tnzi.AI.Infrastructure.Readability;

/// <summary>
/// 基于 SmartReader 的可读性提取器 — Mozilla Readability 的 .NET 实现
/// </summary>
public partial class SmartReaderExtractor : IReadabilityExtractor
{
    private readonly ILogger _logger;

    public SmartReaderExtractor(ILogger<SmartReaderExtractor> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public Task<ArticleContent?> ExtractAsync(string html, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(html))
            return Task.FromResult<ArticleContent?>(null);

        try
        {
            var reader = new Reader("about:blank", html);
            var article = reader.GetArticle();

            if (article.IsReadable && !string.IsNullOrWhiteSpace(article.TextContent))
            {
                _logger.LogDebug("SmartReader extracted article: {Title} ({Length} chars)",
                    article.Title, article.TextContent.Length);

                return Task.FromResult<ArticleContent?>(new ArticleContent(
                    Title: article.Title,
                    TextContent: article.TextContent.Trim(),
                    Author: article.Author,
                    PublishDate: article.PublicationDate,
                    Excerpt: article.Excerpt));
            }

            _logger.LogDebug("SmartReader extraction failed, falling back to tag stripping");
            return Task.FromResult(FallbackTagStrip(html));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SmartReader extraction threw exception, falling back to tag stripping");
            return Task.FromResult(FallbackTagStrip(html));
        }
    }

    private static ArticleContent? FallbackTagStrip(string html)
    {
        var text = html.RemoveHtmlTags();
        text = System.Net.WebUtility.HtmlDecode(text);
        text = MultiWhitespacePattern().Replace(text, " ").Trim();

        return string.IsNullOrWhiteSpace(text) ? null : new ArticleContent(Title: null, TextContent: text);
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiWhitespacePattern();
}
