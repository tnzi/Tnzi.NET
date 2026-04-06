using Tnzi.AI.Rag.FileExtractors;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// TableContentExtractor 单元测试 — 验证 CSV/TSV 文件解析和结构化文本输出
/// </summary>
public class TableContentExtractorTests
{
    #region Supports

    [Theory]
    [InlineData("data.csv", true)]
    [InlineData("data.tsv", true)]
    [InlineData("DATA.CSV", true)]
    [InlineData("DATA.TSV", true)]
    [InlineData("data.Csv", true)]
    [InlineData("test.txt", false)]
    [InlineData("test.pdf", false)]
    [InlineData("test.xlsx", false)]
    [InlineData("test", false)]
    [InlineData("", false)]
    public void Supports_CorrectExtensions(string fileName, bool expected)
    {
        var extractor = new TableContentExtractor();

        extractor.Supports(fileName).ShouldBe(expected);
    }

    #endregion

    #region CSV Extraction

    [Fact]
    public async Task ExtractText_Csv_BasicTable()
    {
        var extractor = new TableContentExtractor();
        var csv = "Name,Age,City\nAlice,30,New York\nBob,25,London";
        using var stream = CreateStream(csv);

        var result = await extractor.ExtractTextAsync(stream, "people.csv");

        result.ShouldBe(
            "Table: people.csv\n" +
            "Headers: Name, Age, City\n" +
            "Row 1: Alice, 30, New York\n" +
            "Row 2: Bob, 25, London");
    }

    [Fact]
    public async Task ExtractText_Csv_SingleRow()
    {
        var extractor = new TableContentExtractor();
        var csv = "Name,Age\nAlice,30";
        using var stream = CreateStream(csv);

        var result = await extractor.ExtractTextAsync(stream, "single.csv");

        result.ShouldBe(
            "Table: single.csv\n" +
            "Headers: Name, Age\n" +
            "Row 1: Alice, 30");
    }

    [Fact]
    public async Task ExtractText_Csv_HeadersOnly()
    {
        var extractor = new TableContentExtractor();
        var csv = "Name,Age,City";
        using var stream = CreateStream(csv);

        var result = await extractor.ExtractTextAsync(stream, "headers.csv");

        result.ShouldBe(
            "Table: headers.csv\n" +
            "Headers: Name, Age, City");
    }

    [Fact]
    public async Task ExtractText_Csv_EmptyFile()
    {
        var extractor = new TableContentExtractor();
        using var stream = new MemoryStream();

        var result = await extractor.ExtractTextAsync(stream, "empty.csv");

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExtractText_Csv_WhitespaceOnly()
    {
        var extractor = new TableContentExtractor();
        var csv = "   \n  \n  ";
        using var stream = CreateStream(csv);

        var result = await extractor.ExtractTextAsync(stream, "whitespace.csv");

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExtractText_Csv_QuotedFields()
    {
        var extractor = new TableContentExtractor();
        var csv = "Name,Description\nAlice,\"Works in New York, USA\"\nBob,\"He said \"\"hello\"\"\"";
        using var stream = CreateStream(csv);

        var result = await extractor.ExtractTextAsync(stream, "quoted.csv");

        result.ShouldBe(
            "Table: quoted.csv\n" +
            "Headers: Name, Description\n" +
            "Row 1: Alice, Works in New York, USA\n" +
            "Row 2: Bob, He said \"hello\"");
    }

    [Fact]
    public async Task ExtractText_Csv_SkipsBlankLines()
    {
        var extractor = new TableContentExtractor();
        var csv = "Name,Age\n\nAlice,30\n\nBob,25\n";
        using var stream = CreateStream(csv);

        var result = await extractor.ExtractTextAsync(stream, "blanks.csv");

        result.ShouldBe(
            "Table: blanks.csv\n" +
            "Headers: Name, Age\n" +
            "Row 1: Alice, 30\n" +
            "Row 2: Bob, 25");
    }

    [Fact]
    public async Task ExtractText_Csv_WindowsLineEndings()
    {
        var extractor = new TableContentExtractor();
        var csv = "Name,Age\r\nAlice,30\r\nBob,25";
        using var stream = CreateStream(csv);

        var result = await extractor.ExtractTextAsync(stream, "windows.csv");

        result.ShouldBe(
            "Table: windows.csv\n" +
            "Headers: Name, Age\n" +
            "Row 1: Alice, 30\n" +
            "Row 2: Bob, 25");
    }

    [Fact]
    public async Task ExtractText_Csv_FilePath_UsesFileNameOnly()
    {
        var extractor = new TableContentExtractor();
        var csv = "A,B\n1,2";
        using var stream = CreateStream(csv);

        var result = await extractor.ExtractTextAsync(stream, "/path/to/data.csv");

        result.ShouldStartWith("Table: data.csv\n");
    }

    #endregion

    #region TSV Extraction

    [Fact]
    public async Task ExtractText_Tsv_BasicTable()
    {
        var extractor = new TableContentExtractor();
        var tsv = "Name\tAge\tCity\nAlice\t30\tNew York\nBob\t25\tLondon";
        using var stream = CreateStream(tsv);

        var result = await extractor.ExtractTextAsync(stream, "people.tsv");

        result.ShouldBe(
            "Table: people.tsv\n" +
            "Headers: Name, Age, City\n" +
            "Row 1: Alice, 30, New York\n" +
            "Row 2: Bob, 25, London");
    }

    [Fact]
    public async Task ExtractText_Tsv_QuotedFields()
    {
        var extractor = new TableContentExtractor();
        var tsv = "Name\tNote\nAlice\t\"Contains\ttab\"";
        using var stream = CreateStream(tsv);

        var result = await extractor.ExtractTextAsync(stream, "tabbed.tsv");

        result.ShouldBe(
            "Table: tabbed.tsv\n" +
            "Headers: Name, Note\n" +
            "Row 1: Alice, Contains\ttab");
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task ExtractText_Cancelled_ThrowsOperationCanceled()
    {
        var extractor = new TableContentExtractor();
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // CancellationToken is checked during ReadToEndAsync
        using var stream = CreateStream("Name,Age\nAlice,30");

        await Should.ThrowAsync<OperationCanceledException>(
            () => extractor.ExtractTextAsync(stream, "cancel.csv", cts.Token));
    }

    #endregion

    #region Helpers

    private static MemoryStream CreateStream(string content) =>
        new(System.Text.Encoding.UTF8.GetBytes(content));

    #endregion
}
