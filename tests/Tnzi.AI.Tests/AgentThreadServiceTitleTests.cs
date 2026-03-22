namespace Tnzi.AI.Tests;

public class AgentThreadServiceTitleTests
{
    [Theory]
    [InlineData("Hello world", "Hello world")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GenerateFallbackTitle_BasicCases(string? input, string? expected)
    {
        var result = AgentThreadService.GenerateFallbackTitle(input, 50);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GenerateFallbackTitle_TruncatesLongAsciiText()
    {
        var input = new string('A', 100);
        var result = AgentThreadService.GenerateFallbackTitle(input, 50);
        Assert.NotNull(result);
        Assert.Equal(53, result.Length); // 50 + "..."
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void GenerateFallbackTitle_TruncatesCjkText()
    {
        var input = string.Concat(Enumerable.Repeat("中", 60));
        var result = AgentThreadService.GenerateFallbackTitle(input, 20);
        Assert.NotNull(result);
        Assert.EndsWith("...", result);
        Assert.True(result.Length <= 23);
    }

    [Fact]
    public void GenerateFallbackTitle_HandlesEmoji()
    {
        var input = "Hello 👋 World 🌍 How are you doing today? This is a long message with emojis 🎉🎊";
        var result = AgentThreadService.GenerateFallbackTitle(input, 20);
        Assert.NotNull(result);
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void GenerateFallbackTitle_TrimsWhitespace()
    {
        var input = "  Hello world  ";
        var result = AgentThreadService.GenerateFallbackTitle(input, 50);
        Assert.Equal("Hello world", result);
    }
}
