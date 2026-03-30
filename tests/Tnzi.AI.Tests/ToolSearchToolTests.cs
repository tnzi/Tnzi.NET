namespace Tnzi.AI.Tests;

public class ToolSearchToolTests
{
    [Fact]
    public void Search_NoRegistry_ReturnsEmptyMessage()
    {
        ToolDeferredRegistry.Current = null;
        var result = ToolSearchTool.Search("test");
        Assert.Contains("No deferred tools", result.ToString()!);
    }

    [Fact]
    public void Search_WithRegistry_ReturnsMatches()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("web_search", "Search the web", null);
        registry.Register("file_read", "Read a file", null);
        ToolDeferredRegistry.Current = registry;

        try
        {
            var result = ToolSearchTool.Search("search");
            var json = System.Text.Json.JsonSerializer.Serialize(result);
            Assert.Contains("web_search", json);
            Assert.DoesNotContain("file_read", json);
        }
        finally
        {
            ToolDeferredRegistry.Current = null;
        }
    }

    [Fact]
    public void Search_MaxResults_Respected()
    {
        var registry = new ToolDeferredRegistry();
        for (var i = 0; i < 10; i++)
            registry.Register($"tool_{i}", $"Tool {i}", null);
        ToolDeferredRegistry.Current = registry;

        try
        {
            var result = ToolSearchTool.Search("tool", maxResults: 3);
            var json = System.Text.Json.JsonSerializer.Serialize(result);
            Assert.Contains("\"matched\":3", json);
        }
        finally
        {
            ToolDeferredRegistry.Current = null;
        }
    }
}
