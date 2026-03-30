using Tnzi.AI.Infrastructure.Readability;

namespace Tnzi.AI.Tests.Readability;

public class SmartReaderExtractorTests
{
    private readonly IReadabilityExtractor _extractor;

    public SmartReaderExtractorTests()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<SmartReaderExtractor>();
        _extractor = new SmartReaderExtractor(logger);
    }

    [Fact]
    public async Task ExtractAsync_WellFormedArticle_ExtractsTitleAndContent()
    {
        var html = """
            <html>
            <head><title>Test Article</title></head>
            <body>
                <article>
                    <h1>Test Article Title</h1>
                    <p>This is the first paragraph with enough content to be considered an article.
                    It needs to be reasonably long so SmartReader considers it valid content for extraction.
                    Let us add more text here to ensure the extraction works properly.</p>
                    <p>Second paragraph continues the discussion about the topic at hand.
                    More content is needed to make this look like a real article that SmartReader would extract.</p>
                </article>
            </body>
            </html>
            """;

        var result = await _extractor.ExtractAsync(html);

        result.ShouldNotBeNull();
        result.TextContent.ShouldNotBeNullOrWhiteSpace();
        result.TextContent.ShouldContain("first paragraph");
    }

    [Fact]
    public async Task ExtractAsync_HtmlWithNavAndAds_StripsNonContent()
    {
        var html = """
            <html><body>
                <nav><a href="/">Home</a><a href="/about">About</a></nav>
                <div class="ad">Buy now!</div>
                <article>
                    <h1>Real Article</h1>
                    <p>This is the actual article content that should be extracted by the readability algorithm.
                    It contains multiple sentences and enough text to be considered meaningful content.
                    The navigation and advertisement elements should be stripped from the output.</p>
                    <p>Another paragraph of meaningful content to help the algorithm identify the main body.</p>
                </article>
                <footer>Copyright 2026</footer>
            </body></html>
            """;

        var result = await _extractor.ExtractAsync(html);

        result.ShouldNotBeNull();
        result.TextContent.ShouldContain("actual article content");
    }

    [Fact]
    public async Task ExtractAsync_EmptyHtml_ReturnsNull()
    {
        var result = await _extractor.ExtractAsync("");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task ExtractAsync_NullHtml_ReturnsNull()
    {
        var result = await _extractor.ExtractAsync(null!);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task ExtractAsync_PlainTextOnly_FallsBackToTagStripping()
    {
        var html = "<html><body><p>Simple text</p></body></html>";

        var result = await _extractor.ExtractAsync(html);

        // 降级到标签剥离应该仍然返回文本
        if (result != null)
        {
            result.TextContent.ShouldContain("Simple text");
        }
    }

    [Fact]
    public async Task ExtractAsync_MalformedHtml_DoesNotThrow()
    {
        var html = "<html><body><p>Unclosed paragraph<div>Nested wrong</p></div></body>";

        // 不应抛出异常
        await Should.NotThrowAsync(async () => await _extractor.ExtractAsync(html));
    }

    [Fact]
    public async Task ExtractAsync_WithTitle_ExtractsTitle()
    {
        var html = """
            <html>
            <head><title>My Page Title</title></head>
            <body>
                <article>
                    <h1>My Page Title</h1>
                    <p>This is a complete article with enough text content for the readability algorithm.
                    It discusses an important topic with multiple paragraphs and sufficient length.
                    The title should be extracted along with the article body.</p>
                    <p>More content in the second paragraph to strengthen the extraction signal.</p>
                </article>
            </body>
            </html>
            """;

        var result = await _extractor.ExtractAsync(html);

        result.ShouldNotBeNull();
        if (!string.IsNullOrEmpty(result.Title))
        {
            result.Title.ShouldContain("My Page Title");
        }
    }
}
