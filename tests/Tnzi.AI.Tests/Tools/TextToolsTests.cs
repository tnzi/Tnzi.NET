namespace Tnzi.AI.Tests.Tools;

/// <summary>
/// TextTools 内置工具功能测试
/// </summary>
public class TextToolsTests
{
    private readonly TextTools _tools;

    public TextToolsTests()
    {
        _tools = new TextTools(NullLogger<TextTools>.Instance);
    }

    #region GetTextStatisticsAsync

    [Fact]
    public async Task GetTextStatistics_NormalText_ReturnsCorrectCounts()
    {
        var result = await _tools.GetTextStatisticsAsync("Hello World");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"character_count\":11");
        json.ShouldContain("\"word_count\":2");
        json.ShouldContain("\"line_count\":1");
    }

    [Fact]
    public async Task GetTextStatistics_MultiLineText_CountsLines()
    {
        var result = await _tools.GetTextStatisticsAsync("Line 1\nLine 2\nLine 3");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"line_count\":3");
        json.ShouldContain("\"word_count\":6");
    }

    [Fact]
    public async Task GetTextStatistics_EmptyText_ReturnsError()
    {
        var result = await _tools.GetTextStatisticsAsync("");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("error");
    }

    [Fact]
    public async Task GetTextStatistics_SingleWord_ReturnsOne()
    {
        var result = await _tools.GetTextStatisticsAsync("Hello");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"word_count\":1");
        json.ShouldContain("\"character_count\":5");
    }

    [Fact]
    public async Task GetTextStatistics_WithTabs_SplitsWords()
    {
        var result = await _tools.GetTextStatisticsAsync("word1\tword2\tword3");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"word_count\":3");
    }

    [Fact]
    public async Task GetTextStatistics_ChineseText_CountsCharacters()
    {
        var result = await _tools.GetTextStatisticsAsync("你好世界");

        var json = JsonSerializer.Serialize(result);
        json.ShouldContain("\"character_count\":4");
    }

    #endregion

    #region ConvertCaseAsync

    [Fact]
    public async Task ConvertCase_ToUpper_ReturnsUpperCase()
    {
        var result = await _tools.ConvertCaseAsync("hello world", "upper");

        result.ShouldBe("HELLO WORLD");
    }

    [Fact]
    public async Task ConvertCase_ToLower_ReturnsLowerCase()
    {
        var result = await _tools.ConvertCaseAsync("HELLO WORLD", "lower");

        result.ShouldBe("hello world");
    }

    [Fact]
    public async Task ConvertCase_ToTitle_ReturnsTitleCase()
    {
        var result = await _tools.ConvertCaseAsync("hello world", "title");

        result.ShouldBe("Hello World");
    }

    [Fact]
    public async Task ConvertCase_UnknownCase_ReturnsOriginal()
    {
        var result = await _tools.ConvertCaseAsync("Hello", "unknown");

        result.ShouldBe("Hello");
    }

    [Fact]
    public async Task ConvertCase_CaseInsensitiveInput_Works()
    {
        var result = await _tools.ConvertCaseAsync("hello", "UPPER");

        result.ShouldBe("HELLO");
    }

    [Fact]
    public async Task ConvertCase_EmptyString_ReturnsEmpty()
    {
        var result = await _tools.ConvertCaseAsync("", "upper");

        result.ShouldBe("");
    }

    #endregion
}
