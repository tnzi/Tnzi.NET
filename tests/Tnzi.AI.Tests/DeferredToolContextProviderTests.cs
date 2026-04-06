namespace Tnzi.AI.Tests;

public class DeferredToolContextProviderTests
{
    private readonly DeferredToolContextProvider _provider;

    public DeferredToolContextProviderTests()
    {
        var logger = NullLogger<DeferredToolContextProvider>.Instance;
        _provider = new DeferredToolContextProvider(logger);
    }

    [Fact]
    public async Task GetContextAsync_NoRegistry_ReturnsEmpty()
    {
        ToolDeferredRegistry.Current = null;

        var result = await _provider.GetContextAsync([]);
        result.ShouldBe(ContextInjection.Empty);
    }

    [Fact]
    public async Task GetContextAsync_EmptyRegistry_ReturnsEmpty()
    {
        ToolDeferredRegistry.Current = new ToolDeferredRegistry();

        try
        {
            var result = await _provider.GetContextAsync([]);
            result.ShouldBe(ContextInjection.Empty);
        }
        finally
        {
            ToolDeferredRegistry.Current = null;
        }
    }

    [Fact]
    public async Task GetContextAsync_AllAlwaysLoad_ReturnsEmpty()
    {
        var registry = new ToolDeferredRegistry();
        // alwaysLoad=true 的工具不会出现在 GetDeferredEntries() 中
        registry.Register("always_tool", "Always loaded", null, null, null, alwaysLoad: true);
        ToolDeferredRegistry.Current = registry;

        try
        {
            var result = await _provider.GetContextAsync([]);
            result.ShouldBe(ContextInjection.Empty);
        }
        finally
        {
            ToolDeferredRegistry.Current = null;
        }
    }

    [Fact]
    public async Task GetContextAsync_WithDeferredTools_InjectsSystemMessage()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("web_search", "Search the web", null, null, null, shouldDefer: true);
        registry.Register("file_read", "Read a file", null, null, null, shouldDefer: true);
        ToolDeferredRegistry.Current = registry;

        try
        {
            var result = await _provider.GetContextAsync([]);

            result.Messages.ShouldNotBeNull();
            result.Messages.Count.ShouldBe(1);

            var message = result.Messages[0];
            message.Role.ShouldBe(ChatRole.System);

            var text = message.Text!;
            text.ShouldContain("<available-deferred-tools>");
            text.ShouldContain("</available-deferred-tools>");
            text.ShouldContain("web_search");
            text.ShouldContain("file_read");
            text.ShouldContain("Search the web");
            text.ShouldContain("Read a file");
            text.ShouldContain("tool_search");
        }
        finally
        {
            ToolDeferredRegistry.Current = null;
        }
    }

    [Fact]
    public async Task GetContextAsync_ContainsUsageExamples()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("my_tool", "My tool", null, null, null, shouldDefer: true);
        ToolDeferredRegistry.Current = registry;

        try
        {
            var result = await _provider.GetContextAsync([]);
            var text = result.Messages![0].Text!;

            // 验证包含使用示例
            text.ShouldContain("select:");
            text.ShouldContain("+prefix");
        }
        finally
        {
            ToolDeferredRegistry.Current = null;
        }
    }

    [Fact]
    public void BuildDeferredToolsSection_CorrectFormat()
    {
        var entries = new List<DeferredToolEntry>
        {
            new("tool_a", "Description A", null, ShouldDefer: true),
            new("tool_b", "Description B", null, ShouldDefer: true),
        };

        var content = DeferredToolContextProvider.BuildDeferredToolsSection(entries, 5);

        content.ShouldContain("<available-deferred-tools>");
        content.ShouldContain("</available-deferred-tools>");
        content.ShouldContain("**tool_a**: Description A");
        content.ShouldContain("**tool_b**: Description B");
        content.ShouldContain("Total available: 5 tools (2 deferred");
    }

    [Fact]
    public void Order_IsAfterSkills()
    {
        // DeferredToolContextProvider 应在 Skills(100) 之后
        _provider.Order.ShouldBeGreaterThan(Tnzi.AI.Constants.ContextProviderOrders.Skills);
    }
}
