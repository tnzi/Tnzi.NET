using Tnzi.AI.Rag.FileExtractors;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// 文件提取器单元测试 — 验证 PlainTextFileExtractor 和 MarkdownFileExtractor
/// </summary>
public class FileExtractorTests
{
    #region PlainTextFileExtractor

    [Theory]
    [InlineData("test.txt", true)]
    [InlineData("data.csv", true)]
    [InlineData("app.log", true)]
    [InlineData("config.json", true)]
    [InlineData("data.xml", true)]
    [InlineData("config.yaml", true)]
    [InlineData("config.yml", true)]
    [InlineData("test.pdf", false)]
    [InlineData("test.docx", false)]
    [InlineData("test.md", false)]
    [InlineData("test", false)]
    [InlineData("", false)]
    public void PlainText_Supports_CorrectExtensions(string fileName, bool expected)
    {
        var extractor = new PlainTextFileExtractor();

        extractor.Supports(fileName).ShouldBe(expected);
    }

    [Fact]
    public void PlainText_Supports_CaseInsensitive()
    {
        var extractor = new PlainTextFileExtractor();

        extractor.Supports("data.TXT").ShouldBeTrue();
        extractor.Supports("data.Csv").ShouldBeTrue();
        extractor.Supports("data.JSON").ShouldBeTrue();
    }

    [Fact]
    public async Task PlainText_ExtractText_ReadsContent()
    {
        var extractor = new PlainTextFileExtractor();
        var content = "Hello, this is plain text content.\nLine 2.";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await extractor.ExtractTextAsync(stream, "test.txt");

        result.ShouldBe(content);
    }

    [Fact]
    public async Task PlainText_ExtractText_EmptyFile_ReturnsEmpty()
    {
        var extractor = new PlainTextFileExtractor();
        using var stream = new MemoryStream();

        var result = await extractor.ExtractTextAsync(stream, "empty.txt");

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task PlainText_ExtractText_Utf8WithBom()
    {
        var extractor = new PlainTextFileExtractor();
        var content = "UTF-8 text with special chars: 中文 日本語";
        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(content))
            .ToArray();
        using var stream = new MemoryStream(bytes);

        var result = await extractor.ExtractTextAsync(stream, "unicode.txt");

        result.ShouldBe(content);
    }

    #endregion

    #region MarkdownFileExtractor

    [Theory]
    [InlineData("readme.md", true)]
    [InlineData("README.MD", true)]
    [InlineData("doc.md", true)]
    [InlineData("test.txt", false)]
    [InlineData("test.pdf", false)]
    [InlineData("test", false)]
    [InlineData("", false)]
    public void Markdown_Supports_CorrectExtensions(string fileName, bool expected)
    {
        var extractor = new MarkdownFileExtractor();

        extractor.Supports(fileName).ShouldBe(expected);
    }

    [Fact]
    public async Task Markdown_ExtractText_ReadsContent()
    {
        var extractor = new MarkdownFileExtractor();
        var content = "# Title\n\nParagraph text.\n\n## Section\n\n- Item 1\n- Item 2";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        var result = await extractor.ExtractTextAsync(stream, "test.md");

        result.ShouldBe(content);
    }

    [Fact]
    public async Task Markdown_ExtractText_EmptyFile_ReturnsEmpty()
    {
        var extractor = new MarkdownFileExtractor();
        using var stream = new MemoryStream();

        var result = await extractor.ExtractTextAsync(stream, "empty.md");

        result.ShouldBeEmpty();
    }

    #endregion
}
