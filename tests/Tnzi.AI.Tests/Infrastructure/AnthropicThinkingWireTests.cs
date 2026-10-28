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

    /// <summary>
    /// adaptive 型号：thinking.type=adaptive、强度落在 output_config.effort、请求推理摘要，线路上不得出现
    /// budget_tokens（这些型号拒收，一开推理就是 400）。
    /// </summary>
    [Theory]
    [InlineData(ReasoningEffort.Low, "low")]
    [InlineData(ReasoningEffort.Medium, "medium")]
    [InlineData(ReasoningEffort.High, "high")]
    [InlineData(ReasoningEffort.Max, "xhigh")]
    public async Task Thinking_OnAnAdaptiveModel_SendsAdaptiveThinkingWithEffort(ReasoningEffort effort, string expectedEffort)
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = effort },
            ProviderName = "Anthropic"
        };

        var (client, handler) = AnthropicWireHarness.CreateClient("claude-opus-5-5");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], new ChatOptions());

        using var doc = JsonDocument.Parse(handler.LastRequestBody.ShouldNotBeNull());
        var thinking = doc.RootElement.GetProperty("thinking");
        thinking.GetProperty("type").GetString().ShouldBe("adaptive");
        thinking.GetProperty("display").GetString().ShouldBe("summarized");
        thinking.TryGetProperty("budget_tokens", out _).ShouldBeFalse();
        doc.RootElement.GetProperty("output_config").GetProperty("effort").GetString().ShouldBe(expectedEffort);
    }

    /// <summary>
    /// 4.6 两种形态都收，但它的 adaptive effort 只有 low / medium / high / max：框架的 Max 档在 adaptive 下落成 xhigh
    /// 会被拒。所以 4.6 走 extended，Max 是 32768 的预算。
    /// </summary>
    [Theory]
    [InlineData("claude-opus-4-6")]
    [InlineData("claude-sonnet-4-6")]
    public async Task Thinking_MaxOnClaude46_StaysOnExtendedThinking(string model)
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.Max },
            ProviderName = "Anthropic"
        };

        var (client, handler) = AnthropicWireHarness.CreateClient(model);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], new ChatOptions());

        using var doc = JsonDocument.Parse(handler.LastRequestBody.ShouldNotBeNull());
        var thinking = doc.RootElement.GetProperty("thinking");
        thinking.GetProperty("type").GetString().ShouldBe("enabled");
        thinking.GetProperty("budget_tokens").GetInt32().ShouldBe(32768);
        doc.RootElement.TryGetProperty("output_config", out _).ShouldBeFalse();
    }

    /// <summary>显式预算（或 ThinkingMiddleware 按 MaxTokens 自动分配的预算）在 adaptive 型号上不得变成 budget_tokens。</summary>
    [Fact]
    public async Task Thinking_ExplicitBudgetOnAnAdaptiveModel_StillSendsAdaptiveThinking()
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.High, BudgetTokens = 5000 },
            ProviderName = "Anthropic"
        };

        var (client, handler) = AnthropicWireHarness.CreateClient("claude-sonnet-5-5");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], new ChatOptions());

        using var doc = JsonDocument.Parse(handler.LastRequestBody.ShouldNotBeNull());
        var thinking = doc.RootElement.GetProperty("thinking");
        thinking.GetProperty("type").GetString().ShouldBe("adaptive");
        thinking.TryGetProperty("budget_tokens", out _).ShouldBeFalse();
        doc.RootElement.GetProperty("output_config").GetProperty("effort").GetString().ShouldBe("high");
        doc.RootElement.GetProperty("model").GetString().ShouldBe("claude-sonnet-5-5");
    }
}
