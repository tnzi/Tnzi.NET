namespace Tnzi.AI.Tests;

public class ToolDeferredRegistryTests
{
    [Fact]
    public void Register_AddsEntry()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("search_web", "Search the web for information", toolObject: null);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void Search_SelectMode_ExactNameMatch()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("web_search", "Search the web", toolObject: null);
        registry.Register("web_fetch", "Fetch a URL", toolObject: null);
        registry.Register("file_read", "Read a file", toolObject: null);

        var results = registry.Search("select:web_search,file_read");
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Name == "web_search");
        Assert.Contains(results, r => r.Name == "file_read");
    }

    [Fact]
    public void Search_KeywordMode_MatchesNameAndDescription()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("web_search", "Search the web for information", toolObject: null);
        registry.Register("image_search", "Search for images", toolObject: null);
        registry.Register("file_read", "Read file content", toolObject: null);

        var results = registry.Search("search");
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Search_PrefixMode_RequiresNameContains()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("web_search", "Search the web", toolObject: null);
        registry.Register("web_fetch", "Fetch a URL", toolObject: null);
        registry.Register("file_read", "Read file", toolObject: null);

        var results = registry.Search("+web fetch");
        Assert.Equal(2, results.Count); // Both web_search and web_fetch contain "web"
        Assert.Equal("web_fetch", results[0].Name); // web_fetch ranks higher ("fetch" in name)
    }

    [Fact]
    public void Search_MaxResults_LimitedTo5()
    {
        var registry = new ToolDeferredRegistry();
        for (var i = 0; i < 10; i++)
            registry.Register($"tool_{i}", $"Tool number {i}", toolObject: null);

        var results = registry.Search("tool");
        Assert.True(results.Count <= 5);
    }
}
