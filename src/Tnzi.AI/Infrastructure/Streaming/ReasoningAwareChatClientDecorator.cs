
namespace Tnzi.AI.Infrastructure.Streaming;

/// <summary>
/// IChatClient decorator that drains reasoning_content chunks emitted by
/// ReasoningCapturingHandler and injects them into the streaming update sequence
/// as TextReasoningContent blocks, before each corresponding MEAI update.
/// Supports multiple reasoning formats via ReasoningExtractingStreamContent:
/// - DeepSeek R1 / Qwen QwQ / OpenAI o-series: delta.reasoning_content
/// - Gemini 2.5: delta.content with extra_content.google.thought=true (requires ThinkingOptions)
/// </summary>
/// <remarks>
/// <para>
/// 同时决定本次调用的推理参数（由 <see cref="ThinkingRequestPolicy"/> 写进请求体）：
/// 管线内以 <c>ThinkingMiddleware</c> 写下的上下文为准，<b>包括它写下的「关」</b>；没有上下文说明调用不经过管线
/// （IAiUtility、评审、摘要），这时才用提供商配置的 <see cref="ProviderOptions.Thinking"/>。
/// 两条路都再按本客户端绑定的模型把一次关：模型不支持推理或推理常开时不注入，因为降级链里换到的模型未必支持推理，
/// 中间件只看得见主模型。注入形态按本客户端绑定的提供商与模型识别（<see cref="ThinkingPayloadFormats"/>），
/// 不按中间件给的条目名。
/// </para>
/// </remarks>
public sealed class ReasoningAwareChatClientDecorator : IChatClient
{
    private readonly IChatClient _inner;
    private readonly ThinkingOptions? _thinking;
    private readonly string? _providerName;
    private readonly string? _model;
    private readonly ThinkingPayloadFormat _format;

    /// <summary>
    /// AsyncLocal channel writer - set by this decorator before making the HTTP call,
    /// written to by ReasoningCapturingHandler when it sees reasoning_content in SSE deltas.
    /// AsyncLocal values flow from parent to child async contexts, so the value set here
    /// is visible inside the DelegatingHandler that runs as part of the HTTP call.
    /// </summary>
    internal static readonly AsyncLocal<ChannelWriter<string>?> ReasoningChannelWriter = new();

    /// <param name="inner">SDK 客户端</param>
    /// <param name="provider">本客户端的提供商条目（管线外的推理配置与注入形态都取自它）</param>
    /// <param name="model">本客户端绑定的模型（单次调用的 <see cref="ChatOptions.ModelId"/> 优先）</param>
    public ReasoningAwareChatClientDecorator(IChatClient inner, ProviderOptions? provider = null, string? model = null)
    {
        _inner = Check.NotNull(inner);
        _thinking = provider?.Thinking;
        _providerName = provider?.Name;
        _model = model;
        _format = ThinkingPayloadFormats.Resolve(provider, model);
    }

    /// <summary>
    /// 本次调用交给 <see cref="ThinkingRequestPolicy"/> 的上下文：恒非 null（「关」也写明），形态恒为本客户端的形态。
    /// </summary>
    private ThinkingRequestContext ResolveRequestContext(ChatOptions? options)
    {
        var current = ThinkingRequestPolicy.RequestContext.Value;
        var model = options?.ModelId ?? _model;

        var thinking = current != null ? current.Thinking : _thinking;
        if (thinking is not { Effort: not ReasoningEffort.None }
            || !ModelCapabilities.SupportsReasoning(model)
            || ModelCapabilities.IsAlwaysOnReasoning(model))
        {
            thinking = new ThinkingOptions { Effort = ReasoningEffort.None };
        }

        return new ThinkingRequestContext
        {
            Thinking = thinking,
            ProviderName = current?.ProviderName ?? _providerName,
            Format = _format
        };
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
        ThinkingRequestPolicy.RequestContext.Value = ResolveRequestContext(options);

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
        ThinkingRequestPolicy.RequestContext.Value = ResolveRequestContext(options);

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
