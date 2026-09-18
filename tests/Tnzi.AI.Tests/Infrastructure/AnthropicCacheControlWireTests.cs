using Anthropic.Models.Messages;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// 线路级断言：<see cref="PromptCachingMiddleware"/> 写下的断点经真实 SDK 适配器出现在出站 JSON 的
/// system / content / tool 块上（见 <see cref="AnthropicWireHarness"/>）。
/// </summary>
public class AnthropicCacheControlWireTests
{
    [Fact]
    public async Task CacheControl_OnSystemAndUserBlocks_AppearsInOutboundJson()
    {
        var (client, handler) = AnthropicWireHarness.CreateClient();

        var system = new ChatMessage(ChatRole.System, "You are a careful assistant.");
        var user = new ChatMessage(ChatRole.User, "hi");
        PromptCachingMiddleware.SetCacheBreakpoint(system, Ttl.Ttl1h);
        PromptCachingMiddleware.SetCacheBreakpoint(user, null);

        await client.GetResponseAsync([system, user], new ChatOptions());

        var body = handler.LastRequestBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(body);

        var systemBlock = doc.RootElement.GetProperty("system")[0];
        systemBlock.GetProperty("cache_control").GetProperty("type").GetString().ShouldBe("ephemeral");
        systemBlock.GetProperty("cache_control").GetProperty("ttl").GetString().ShouldBe("1h");

        var userBlock = doc.RootElement.GetProperty("messages")[0].GetProperty("content")[0];
        userBlock.GetProperty("cache_control").GetProperty("type").GetString().ShouldBe("ephemeral");
    }

    [Fact]
    public async Task CacheControl_OnLastToolDefinition_AppearsInOutboundJson()
    {
        var (client, handler) = AnthropicWireHarness.CreateClient();

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(() => "a", "tool_a"),
            AIFunctionFactory.Create(() => "b", "tool_b")
        };
        PromptCachingMiddleware.MarkLastToolDefinitionForCaching(tools);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], new ChatOptions { Tools = tools });

        var body = handler.LastRequestBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(body);
        var toolsJson = doc.RootElement.GetProperty("tools");
        toolsJson.GetArrayLength().ShouldBe(2);
        toolsJson[0].TryGetProperty("cache_control", out _).ShouldBeFalse();
        toolsJson[1].GetProperty("cache_control").GetProperty("type").GetString().ShouldBe("ephemeral");
    }
}
