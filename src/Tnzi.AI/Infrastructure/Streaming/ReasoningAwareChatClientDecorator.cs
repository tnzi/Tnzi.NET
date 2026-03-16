using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Tnzi.AI.Infrastructure.Streaming;

/// <summary>
/// IChatClient decorator that drains reasoning_content chunks emitted by
/// ReasoningCapturingHandler and injects them into the streaming update sequence
/// as TextReasoningContent blocks, before each corresponding MEAI update.
/// Supports multiple reasoning formats via ReasoningExtractingStreamContent:
/// - DeepSeek R1 / Qwen QwQ / OpenAI o-series: delta.reasoning_content
/// - Gemini 2.5: delta.content with extra_content.google.thought=true (requires ThinkingOptions)
/// </summary>
public sealed class ReasoningAwareChatClientDecorator : IChatClient
{
    private readonly IChatClient _inner;
    private readonly ThinkingOptions? _thinking;

    /// <summary>
    /// AsyncLocal channel writer — set by this decorator before making the HTTP call,
    /// written to by ReasoningCapturingHandler when it sees reasoning_content in SSE deltas.
    /// AsyncLocal values flow from parent to child async contexts, so the value set here
    /// is visible inside the DelegatingHandler that runs as part of the HTTP call.
    /// </summary>
    internal static readonly AsyncLocal<ChannelWriter<string>?> ReasoningChannelWriter = new();

    public ReasoningAwareChatClientDecorator(IChatClient inner, ThinkingOptions? thinking = null)
    {
        _inner = Check.NotNull(inner);
        _thinking = thinking;
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

        // Save and restore AsyncLocal values so nested decorator calls (Router → Specialist)
        // don't lose the externally-set thinking context.
        var previousWriter = ReasoningChannelWriter.Value;
        var previousContext = ThinkingRequestPolicy.RequestContext.Value;

        ReasoningChannelWriter.Value = channel.Writer;

        // Set thinking request context so ThinkingRequestPolicy can inject provider-specific params.
        // Preserve any externally-set context (e.g., per-request override from ThinkingMiddleware).
        if (ThinkingRequestPolicy.RequestContext.Value == null)
        {
            ThinkingRequestPolicy.RequestContext.Value = _thinking is { Effort: not ReasoningEffort.None }
                ? new ThinkingRequestContext { Thinking = _thinking }
                : null;
        }

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
            ReasoningChannelWriter.Value = previousWriter;
            ThinkingRequestPolicy.RequestContext.Value = previousContext;
            channel.Writer.TryComplete();
        }
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Set thinking request context for non-streaming calls too,
        // so ThinkingRequestPolicy can inject provider-specific params (e.g., Gemini thinking_config).
        var previousContext = ThinkingRequestPolicy.RequestContext.Value;

        if (ThinkingRequestPolicy.RequestContext.Value == null && _thinking is { Effort: not ReasoningEffort.None })
        {
            ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext { Thinking = _thinking };
        }

        try
        {
            return await _inner.GetResponseAsync(chatMessages, options, cancellationToken);
        }
        finally
        {
            ThinkingRequestPolicy.RequestContext.Value = previousContext;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => _inner.GetService(serviceType, serviceKey);

    public void Dispose() => _inner.Dispose();
}
