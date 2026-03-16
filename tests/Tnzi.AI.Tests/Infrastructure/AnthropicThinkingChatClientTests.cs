using Moq;
using Tnzi.AI.Infrastructure.Providers;
using Tnzi.AI.Infrastructure.Streaming;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// AnthropicThinkingChatClient 单元测试
/// </summary>
public class AnthropicThinkingChatClientTests : IDisposable
{
    private readonly Mock<IChatClient> _innerClient = new();

    public AnthropicThinkingChatClientTests()
    {
        // 确保每个测试开始时 AsyncLocal 是干净的
        ThinkingRequestPolicy.RequestContext.Value = null;

        _innerClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([]));

        _innerClient.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncEnumerable([CreateUpdate("ok")]));
    }

    public void Dispose()
    {
        ThinkingRequestPolicy.RequestContext.Value = null;
    }

    [Fact]
    public async Task GetResponseAsync_NoThinkingContext_PassesOptionsUnchanged()
    {
        var client = new AnthropicThinkingChatClient(_innerClient.Object);
        var originalOptions = new ChatOptions { Temperature = 0.7f };

        await client.GetResponseAsync([], originalOptions);

        _innerClient.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.Is<ChatOptions?>(o => o == originalOptions),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetResponseAsync_WithThinkingContext_InjectsAdditionalProperties()
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.High },
            ProviderName = "Anthropic"
        };

        var client = new AnthropicThinkingChatClient(_innerClient.Object);
        ChatOptions? capturedOptions = null;

        _innerClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(new ChatResponse([]));

        await client.GetResponseAsync([], new ChatOptions());

        capturedOptions.ShouldNotBeNull();
        capturedOptions.AdditionalProperties.ShouldNotBeNull();
        capturedOptions.AdditionalProperties.ShouldContainKey("thinking");

        var thinking = capturedOptions.AdditionalProperties["thinking"].ShouldBeOfType<Dictionary<string, object>>();
        thinking["type"].ShouldBe("enabled");
        thinking["budget_tokens"].ShouldBe(16384);
    }

    [Fact]
    public async Task GetResponseAsync_EffortNone_PassesOptionsUnchanged()
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.None },
            ProviderName = "Anthropic"
        };

        var client = new AnthropicThinkingChatClient(_innerClient.Object);
        var originalOptions = new ChatOptions();

        await client.GetResponseAsync([], originalOptions);

        _innerClient.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.Is<ChatOptions?>(o => o == originalOptions),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ReasoningEffort.Low, 1024)]
    [InlineData(ReasoningEffort.Medium, 8192)]
    [InlineData(ReasoningEffort.High, 16384)]
    [InlineData(ReasoningEffort.Max, 32768)]
    public async Task GetResponseAsync_BudgetTokensMapping_CorrectValues(ReasoningEffort effort, int expectedBudget)
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = effort },
            ProviderName = "Anthropic"
        };

        var client = new AnthropicThinkingChatClient(_innerClient.Object);
        ChatOptions? capturedOptions = null;

        _innerClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(new ChatResponse([]));

        await client.GetResponseAsync([], new ChatOptions());

        capturedOptions.ShouldNotBeNull();
        var thinking = capturedOptions.AdditionalProperties!["thinking"].ShouldBeOfType<Dictionary<string, object>>();
        thinking["budget_tokens"].ShouldBe(expectedBudget);
    }

    [Fact]
    public async Task GetResponseAsync_ExplicitBudgetTokens_OverridesDefault()
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.Low, BudgetTokens = 5000 },
            ProviderName = "Anthropic"
        };

        var client = new AnthropicThinkingChatClient(_innerClient.Object);
        ChatOptions? capturedOptions = null;

        _innerClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(new ChatResponse([]));

        await client.GetResponseAsync([], new ChatOptions());

        capturedOptions.ShouldNotBeNull();
        var thinking = capturedOptions.AdditionalProperties!["thinking"].ShouldBeOfType<Dictionary<string, object>>();
        thinking["budget_tokens"].ShouldBe(5000);
    }

    [Fact]
    public async Task GetResponseAsync_WithThinking_DoesNotMutateOriginalOptions()
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.High },
            ProviderName = "Anthropic"
        };

        var client = new AnthropicThinkingChatClient(_innerClient.Object);
        var originalOptions = new ChatOptions { Temperature = 0.5f };

        _innerClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([]));

        await client.GetResponseAsync([], originalOptions);

        // 原始 options 不应被修改
        originalOptions.AdditionalProperties.ShouldBeNull();
    }

    [Fact]
    public async Task GetResponseAsync_NullOptions_CreatesNewOptionsWithThinking()
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.Medium },
            ProviderName = "Anthropic"
        };

        var client = new AnthropicThinkingChatClient(_innerClient.Object);
        ChatOptions? capturedOptions = null;

        _innerClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(new ChatResponse([]));

        await client.GetResponseAsync([], null);

        capturedOptions.ShouldNotBeNull();
        capturedOptions.AdditionalProperties.ShouldNotBeNull();
        capturedOptions.AdditionalProperties.ShouldContainKey("thinking");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NoThinkingContext_PassesOptionsUnchanged()
    {
        var client = new AnthropicThinkingChatClient(_innerClient.Object);
        var originalOptions = new ChatOptions { Temperature = 0.7f };

        await client.GetStreamingResponseAsync([], originalOptions).ToListAsync();

        _innerClient.Verify(c => c.GetStreamingResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.Is<ChatOptions?>(o => o == originalOptions),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WithThinkingContext_InjectsAdditionalPropertiesWithoutMutatingOriginalOptions()
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.High },
            ProviderName = "Anthropic"
        };

        var client = new AnthropicThinkingChatClient(_innerClient.Object);
        var originalOptions = new ChatOptions { Temperature = 0.5f };
        ChatOptions? capturedOptions = null;

        _innerClient.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .Returns(CreateAsyncEnumerable([CreateUpdate("stream")]));

        var updates = await client.GetStreamingResponseAsync([], originalOptions).ToListAsync();

        updates.Count.ShouldBe(1);
        updates[0].Contents.Count.ShouldBe(1);
        updates[0].Contents[0].ShouldBeOfType<TextContent>().Text.ShouldBe("stream");

        capturedOptions.ShouldNotBeNull();
        capturedOptions.ShouldNotBeSameAs(originalOptions);
        capturedOptions.AdditionalProperties.ShouldNotBeNull();
        capturedOptions.AdditionalProperties.ShouldContainKey("thinking");

        var thinking = capturedOptions.AdditionalProperties["thinking"].ShouldBeOfType<Dictionary<string, object>>();
        thinking["type"].ShouldBe("enabled");
        thinking["budget_tokens"].ShouldBe(16384);

        originalOptions.AdditionalProperties.ShouldBeNull();
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateAsyncEnumerable(IEnumerable<ChatResponseUpdate> updates)
    {
        foreach (var update in updates)
        {
            await Task.Yield();
            yield return update;
        }
    }

    private static ChatResponseUpdate CreateUpdate(string text) => new()
    {
        Role = ChatRole.Assistant,
        Contents = [new TextContent(text)]
    };
}
