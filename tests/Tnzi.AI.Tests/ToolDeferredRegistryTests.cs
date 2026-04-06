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
    public void Register_WithMetadata_PersistsDeferredToolFlags()
    {
        var registry = new ToolDeferredRegistry();

        registry.Register(
            "mcp_search",
            "Search remote MCP server",
            toolObject: null,
            searchHint: "mcp remote search",
            aliases: ["remote_search", "server_search"],
            shouldDefer: true,
            alwaysLoad: false,
            priority: 9,
            serverHint: "docs");

        var entry = Assert.Single(registry.Entries);
        Assert.Equal("mcp remote search", entry.SearchHint);
        Assert.Equal(["remote_search", "server_search"], entry.Aliases);
        Assert.True(entry.ShouldDefer);
        Assert.False(entry.AlwaysLoad);
        Assert.Equal(9, entry.Priority);
        Assert.Equal("docs", entry.ServerHint);
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

    [Fact]
    public void Search_SameScore_PrefersHigherPriority()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("tool_low", "General tool", null, "alpha", ["alpha"], priority: 1);
        registry.Register("tool_high", "General tool", null, "alpha", ["alpha"], priority: 10);

        var results = registry.Search("alpha");

        Assert.Equal("tool_high", results[0].Name);
    }

    [Fact]
    public void LoadTools_MarksToolsAsLoaded_AndExcludesThemFromDeferredSet()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("tool_a", "Deferred A", null, null, null, shouldDefer: true);
        registry.Register("tool_b", "Deferred B", null, null, null, shouldDefer: true);

        var loaded = registry.LoadTools(["tool_a"]);

        Assert.Single(loaded);
        Assert.True(registry.IsLoaded("tool_a"));
        Assert.False(registry.IsLoaded("tool_b"));
        Assert.DoesNotContain(registry.GetDeferredEntries(), x => x.Name == "tool_a");
        Assert.Contains(registry.GetDeferredEntries(), x => x.Name == "tool_b");
    }

    [Fact]
    public void Register_AlwaysLoad_MarksEntryAsLoadedImmediately()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("tool_a", "Always load tool", null, null, null, shouldDefer: true, alwaysLoad: true);

        Assert.True(registry.IsLoaded("tool_a"));
        Assert.Empty(registry.GetDeferredEntries());
    }
}
