namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// 线路级断言：<see cref="AnthropicThinkingChatClient"/> 写入的 thinking 经真实 SDK 适配器
/// 出现在出站 JSON 里（见 <see cref="AnthropicWireHarness"/>）。
/// </summary>
public class AnthropicThinkingWireTests : IDisposable
{
    private const string Model = AnthropicWireHarness.Model;

    public AnthropicThinkingWireTests()
    {
        ThinkingRequestPolicy.RequestContext.Value = null;
    }

    public void Dispose()
    {
        ThinkingRequestPolicy.RequestContext.Value = null;
    }

    [Theory]
    [InlineData(ReasoningEffort.Low, 1024)]
    [InlineData(ReasoningEffort.Medium, 8192)]
    [InlineData(ReasoningEffort.High, 16384)]
    [InlineData(ReasoningEffort.Max, 32768)]
    public async Task Thinking_ByEffort_AppearsInOutboundJson(ReasoningEffort effort, int expectedBudget)
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = effort },
            ProviderName = "Anthropic"
        };

        var (client, handler) = AnthropicWireHarness.CreateClient();

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], new ChatOptions());

        var body = handler.LastRequestBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(body);
        var thinking = doc.RootElement.GetProperty("thinking");
        thinking.GetProperty("type").GetString().ShouldBe("enabled");
        thinking.GetProperty("budget_tokens").GetInt32().ShouldBe(expectedBudget);
        // max_tokens must exceed budget_tokens or the API rejects the request
        doc.RootElement.GetProperty("max_tokens").GetInt32().ShouldBeGreaterThan(expectedBudget);
    }

    [Fact]
    public async Task Thinking_ExplicitBudget_AppearsInOutboundJson()
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.Low, BudgetTokens = 5000 },
            ProviderName = "Anthropic"
        };

        var (client, handler) = AnthropicWireHarness.CreateClient();

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], new ChatOptions { Temperature = 0.3f });

        var body = handler.LastRequestBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(body);
        var thinking = doc.RootElement.GetProperty("thinking");
        thinking.GetProperty("type").GetString().ShouldBe("enabled");
        thinking.GetProperty("budget_tokens").GetInt32().ShouldBe(5000);
        doc.RootElement.GetProperty("max_tokens").GetInt32().ShouldBeGreaterThan(5000);
        doc.RootElement.GetProperty("model").GetString().ShouldBe(Model);
        // the raw-representation path must not drop the ordinary options
        doc.RootElement.GetProperty("temperature").GetSingle().ShouldBe(0.3f);
        doc.RootElement.GetProperty("messages").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Thinking_EffortNone_NotInOutboundJson()
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.None },
            ProviderName = "Anthropic"
        };

        var (client, handler) = AnthropicWireHarness.CreateClient();

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], new ChatOptions());

        var body = handler.LastRequestBody.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("thinking", out _).ShouldBeFalse();
    }
}
