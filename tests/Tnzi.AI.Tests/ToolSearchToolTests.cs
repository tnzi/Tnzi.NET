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

    [Fact]
    public void Search_DefaultLoad_LoadsMatchedToolsIntoCurrentRegistry()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("web_search", "Search the web", null, "search web", ["web"], shouldDefer: true);
        registry.Register("file_read", "Read a file", null, "read file", ["file"], shouldDefer: true);
        ToolDeferredRegistry.Current = registry;

        try
        {
            var result = ToolSearchTool.Search("web");
            var json = System.Text.Json.JsonSerializer.Serialize(result);

            Assert.Contains("\"loaded_tools\":[\"web_search\"]", json);
            Assert.True(registry.IsLoaded("web_search"));
            Assert.False(registry.IsLoaded("file_read"));
        }
        finally
        {
            ToolDeferredRegistry.Current = null;
        }
    }

    [Fact]
    public void Search_LoadFalse_DoesNotMarkToolsAsLoaded()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("web_search", "Search the web", null, "search web", ["web"], shouldDefer: true);
        ToolDeferredRegistry.Current = registry;

        try
        {
            var result = ToolSearchTool.Search("web", load: false);
            var json = System.Text.Json.JsonSerializer.Serialize(result);

            Assert.Contains("\"loaded_count\":0", json);
            Assert.False(registry.IsLoaded("web_search"));
        }
        finally
        {
            ToolDeferredRegistry.Current = null;
        }
    }

    [Fact]
    public void Search_WithAIFunction_ReturnsParametersSchema()
    {
        // 创建一个真实的 AIFunction 来验证 schema 提取
        var aiFunction = AIFunctionFactory.Create(
            (string query, int maxResults) => "result",
            new AIFunctionFactoryOptions { Name = "test_func", Description = "A test function" });

        var registry = new ToolDeferredRegistry();
        registry.Register("test_func", "A test function", aiFunction);
        ToolDeferredRegistry.Current = registry;

        try
        {
            var result = ToolSearchTool.Search("test_func");
            var json = JsonSerializer.Serialize(result);

            // 验证 parameters_schema 字段存在且包含参数定义
            Assert.Contains("parameters_schema", json);
            Assert.Contains("query", json);
            Assert.Contains("maxResults", json);
        }
        finally
        {
            ToolDeferredRegistry.Current = null;
        }
    }

    [Fact]
    public void Search_WithNullToolObject_ReturnsNullParametersSchema()
    {
        var registry = new ToolDeferredRegistry();
        registry.Register("no_schema_tool", "A tool without schema", null);
        ToolDeferredRegistry.Current = registry;

        try
        {
            var result = ToolSearchTool.Search("no_schema_tool");
            var json = JsonSerializer.Serialize(result);

            // parameters_schema 应为 null
            Assert.Contains("\"parameters_schema\":null", json);
        }
        finally
        {
            ToolDeferredRegistry.Current = null;
        }
    }

    [Fact]
    public void Search_WithAITool_ReturnsParametersSchema()
    {
        // AIFunction 也是 AITool，但通过 AITool 引用来测试 GetService 路径
        var aiFunction = AIFunctionFactory.Create(
            (string input) => input,
            new AIFunctionFactoryOptions { Name = "echo", Description = "Echo input" });

        // AIFunction 继承自 AITool，直接作为 ToolObject 使用
        var registry = new ToolDeferredRegistry();
        registry.Register("echo", "Echo input", (AITool)aiFunction);
        ToolDeferredRegistry.Current = registry;

        try
        {
            var result = ToolSearchTool.Search("echo");
            var json = JsonSerializer.Serialize(result);

            Assert.Contains("parameters_schema", json);
            Assert.Contains("input", json);
        }
        finally
        {
            ToolDeferredRegistry.Current = null;
        }
    }
}
