namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// The OpenAI-compatible path (also used for a Claude model behind an OpenAI-compatible endpoint, and for the
/// utility's caller-supplied provider): the decorator drops sampling parameters for models that reject them.
/// </summary>
public class SamplingParameterGuardTests
{
    [Fact]
    public async Task OpenAiCompatibleDecorator_DropsSamplingParameters_ForAModelThatRejectsThem()
    {
        var inner = new CapturingChatClient();
        var client = new ReasoningAwareChatClientDecorator(inner, new ProviderOptions { Name = "Gateway" }, "claude-sonnet-5-5");

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")],
            new ChatOptions { Temperature = 0.3f, TopP = 0.9f, TopK = 20 });

        inner.Last.ShouldNotBeNull();
        inner.Last.Temperature.ShouldBeNull();
        inner.Last.TopP.ShouldBeNull();
        inner.Last.TopK.ShouldBeNull();
    }

    [Fact]
    public async Task OpenAiCompatibleDecorator_KeepsSamplingParameters_ForAModelThatAcceptsThem()
    {
        var inner = new CapturingChatClient();
        var client = new ReasoningAwareChatClientDecorator(inner, new ProviderOptions { Name = "OpenAI" }, "gpt-4.1-mini");
        var options = new ChatOptions { Temperature = 0.3f };

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], options);

        inner.Last.ShouldBeSameAs(options, "nothing to drop: the caller's options go through untouched");
        inner.Last!.Temperature.ShouldBe(0.3f);
    }

    [Fact]
    public async Task OpenAiCompatibleDecorator_Streaming_DropsTemperature()
    {
        var inner = new CapturingChatClient();
        var client = new ReasoningAwareChatClientDecorator(inner, new ProviderOptions { Name = "Gateway" }, "claude-haiku-5-5");

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")],
                           new ChatOptions { Temperature = 0.3f }))
        {
        }

        inner.Last!.Temperature.ShouldBeNull();
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public ChatOptions? Last { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Last = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Last = options;
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
