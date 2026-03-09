using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Tnzi.AI.Infrastructure.Streaming;

/// <summary>
/// IChatClient decorator that drains reasoning_content chunks emitted by
/// ReasoningCapturingHandler and injects them into the streaming update sequence
/// as TextReasoningContent blocks, before each corresponding MEAI update.
/// </summary>
public sealed class ReasoningAwareChatClientDecorator : IChatClient
{
    private readonly IChatClient _inner;

    /// <summary>
    /// AsyncLocal channel writer — set by this decorator before making the HTTP call,
    /// written to by ReasoningCapturingHandler when it sees reasoning_content in SSE deltas.
    /// AsyncLocal values flow from parent to child async contexts, so the value set here
    /// is visible inside the DelegatingHandler that runs as part of the HTTP call.
    /// </summary>
    internal static readonly AsyncLocal<ChannelWriter<string>?> ReasoningChannelWriter = new();

    public ReasoningAwareChatClientDecorator(IChatClient inner)
    {
        _inner = inner;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        ReasoningChannelWriter.Value = channel.Writer;

        try
        {
            await foreach (var update in _inner.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
            {
                // Drain reasoning accumulated before this MEAI update.
                // Reasoning bytes are written to the channel BEFORE being written to the Pipe,
                // so they always arrive here before the corresponding MEAI update yields.
                while (channel.Reader.TryRead(out var reasoning))
                {
                    yield return new ChatResponseUpdate
                    {
                        Contents = [new TextReasoningContent(reasoning)]
                    };
                }

                yield return update;
            }

            // Drain any reasoning remaining after the stream ends
            while (channel.Reader.TryRead(out var reasoning))
            {
                yield return new ChatResponseUpdate
                {
                    Contents = [new TextReasoningContent(reasoning)]
                };
            }
        }
        finally
        {
            ReasoningChannelWriter.Value = null;
            channel.Writer.TryComplete();
        }
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => _inner.GetResponseAsync(chatMessages, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null)
        => _inner.GetService(serviceType, serviceKey);

    public void Dispose() => _inner.Dispose();
}
