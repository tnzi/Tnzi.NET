namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// 线路级断言：发往 Anthropic 的请求体里有没有 <c>temperature</c> / <c>top_p</c> / <c>top_k</c>。
/// </summary>
/// <remarks>
/// 新一代 Claude 型号对带 <c>temperature</c> 的请求答 400（「`temperature` is deprecated for this model」），
/// 所以这些断言读的是真实 SDK 适配器写出的 JSON，而不是我们传进去的 <see cref="ChatOptions"/>。
/// </remarks>
public class AnthropicSamplingWireTests : IDisposable
{
    public AnthropicSamplingWireTests()
    {
        ThinkingRequestPolicy.RequestContext.Value = null;
    }

    public void Dispose()
    {
        ThinkingRequestPolicy.RequestContext.Value = null;
    }

    // ---------------------------------------------------------------- the utility call

    [Fact]
    public async Task UtilityCall_ByDefault_SendsNoTemperature()
    {
        var (service, handler) = Utility("claude-haiku-5-5", new AiUtilityOptions());

        await service.ExecuteForResponseAsync("Name this conversation.", "hello");

        using var doc = Body(handler);
        doc.RootElement.TryGetProperty("temperature", out _).ShouldBeFalse(
            "the utility default is not to send temperature: newer Claude models answer 400 to it");
        doc.RootElement.GetProperty("model").GetString().ShouldBe("claude-haiku-5-5");
    }

    [Theory]
    [InlineData("claude-haiku-5-5")]
    [InlineData("claude-sonnet-5-5")]
    [InlineData("claude-fable-5-1")]
    public async Task UtilityCall_ConfiguredTemperature_IsNotSentToAModelThatRejectsIt(string model)
    {
        var (service, handler) = Utility(model, new AiUtilityOptions { Temperature = 0.3 });

        await service.ExecuteForResponseAsync("Name this conversation.", "hello");

        using var doc = Body(handler);
        doc.RootElement.TryGetProperty("temperature", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task UtilityCall_ConfiguredTemperature_IsHonouredForAModelThatAcceptsIt()
    {
        var (service, handler) = Utility("claude-sonnet-4-5", new AiUtilityOptions { Temperature = 0.3 });

        await service.ExecuteForResponseAsync("Name this conversation.", "hello");

        using var doc = Body(handler);
        doc.RootElement.GetProperty("temperature").GetSingle().ShouldBe(0.3f);
    }

    [Fact]
    public async Task PerCallModelId_IsJudgedByTheModelActuallyCalled()
    {
        // the client is bound to a model that accepts temperature; this call goes to one that does not
        var (client, handler) = AnthropicWireHarness.CreateClient("claude-sonnet-4-5");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
            new ChatOptions { ModelId = "claude-haiku-5-5", Temperature = 0.3f });

        using var doc = Body(handler);
        doc.RootElement.GetProperty("model").GetString().ShouldBe("claude-haiku-5-5");
        doc.RootElement.TryGetProperty("temperature", out _).ShouldBeFalse();
    }

    // ---------------------------------------------------------------- any caller (agent runs, sub-modules)

    [Fact]
    public async Task AgentStyleCall_AllSamplingParameters_AreDroppedForAModelThatRejectsThem()
    {
        var (client, handler) = AnthropicWireHarness.CreateClient("claude-sonnet-5-5");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
            new ChatOptions { Temperature = 0.7f, TopP = 0.9f, TopK = 40 });

        using var doc = Body(handler);
        doc.RootElement.TryGetProperty("temperature", out _).ShouldBeFalse();
        doc.RootElement.TryGetProperty("top_p", out _).ShouldBeFalse();
        doc.RootElement.TryGetProperty("top_k", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task AgentStyleCall_SamplingParameters_ReachAModelThatAcceptsThem()
    {
        var (client, handler) = AnthropicWireHarness.CreateClient("claude-sonnet-4-5");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
            new ChatOptions { Temperature = 0.7f, TopK = 40 });

        using var doc = Body(handler);
        doc.RootElement.GetProperty("temperature").GetSingle().ShouldBe(0.7f);
        doc.RootElement.GetProperty("top_k").GetInt32().ShouldBe(40);
    }

    [Fact]
    public async Task ThinkingOn_SamplingParameters_AreDroppedEvenForAModelThatAcceptsThem()
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.Medium },
            ProviderName = "Anthropic"
        };
        var (client, handler) = AnthropicWireHarness.CreateClient("claude-sonnet-4-5");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
            new ChatOptions { Temperature = 0.7f, TopK = 40 });

        using var doc = Body(handler);
        doc.RootElement.GetProperty("thinking").GetProperty("type").GetString().ShouldBe("enabled");
        doc.RootElement.TryGetProperty("temperature", out _).ShouldBeFalse();
        doc.RootElement.TryGetProperty("top_k", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task TheCallersOptions_AreNotModified()
    {
        var (client, _) = AnthropicWireHarness.CreateClient("claude-sonnet-5-5");
        var options = new ChatOptions { Temperature = 0.7f };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        options.Temperature.ShouldBe(0.7f);
    }

    // ---------------------------------------------------------------- helpers

    private static (AiUtilityService Service, AnthropicWireHarness.CapturingHandler Handler) Utility(string model, AiUtilityOptions options)
    {
        var (client, handler) = AnthropicWireHarness.CreateClient(model);
        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>())).Returns(client);
        factory.Setup(f => f.GetDefaultModel(It.IsAny<string?>())).Returns(model);

        var monitor = new Mock<IOptionsMonitor<AiUtilityOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);

        return (new AiUtilityService(factory.Object, monitor.Object), handler);
    }

    private static JsonDocument Body(AnthropicWireHarness.CapturingHandler handler)
        => JsonDocument.Parse(handler.LastRequestBody.ShouldNotBeNull());
}
