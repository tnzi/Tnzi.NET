
namespace Tnzi.AI.Infrastructure.Streaming;

/// <summary>
/// OpenAI SDK PipelinePolicy that handles both request-side thinking injection
/// and response-side reasoning extraction in a single policy.
/// <para>
/// Request: Injects provider-specific thinking parameters (e.g., Gemini extra_body)
/// into the JSON body when ThinkingRequestContext is set via AsyncLocal.
/// </para>
/// <para>
/// Response: Wraps the streaming response to extract reasoning_content (DeepSeek/OpenAI format)
/// and extra_content.google.thought (Gemini format), forwarding extracted text to the
/// AsyncLocal ChannelWriter set by ReasoningAwareChatClientDecorator.
/// </para>
/// </summary>
public sealed class ThinkingRequestPolicy : PipelinePolicy
{
    /// <summary>
    /// AsyncLocal context set by ReasoningAwareChatClientDecorator before the HTTP call.
    /// </summary>
    public static readonly AsyncLocal<ThinkingRequestContext?> RequestContext = new();

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        InjectThinkingParams(message);
        ProcessNext(message, pipeline, currentIndex);
        WrapResponseForReasoningExtraction(message);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        InjectThinkingParams(message);
        await ProcessNextAsync(message, pipeline, currentIndex);
        WrapResponseForReasoningExtraction(message);
    }

    /// <summary>
    /// Inject provider-specific thinking params into request body when configured.
    /// Routes by <see cref="ThinkingRequestContext.Format"/> (stamped by <see cref="ReasoningAwareChatClientDecorator"/>
    /// from the provider entry and model it is bound to); a context without a format falls back to the entry name.
    /// </summary>
    internal static void InjectThinkingParams(PipelineMessage message)
    {
        var context = RequestContext.Value;
        var format = context?.Format ?? ThinkingPayloadFormats.Resolve(new ProviderOptions { Name = context?.ProviderName ?? string.Empty }, model: null);

        // DeepSeek: always-on reasoning, no body injection needed.
        // BUT multi-turn tool calls require all assistant messages to carry reasoning_content.
        // MEAI doesn't serialize reasoning content into outgoing messages, so we patch it here.
        // Detect via the resolved format OR by request URL - the latter handles always-on
        // reasoning models (e.g., deepseek-reasoner) reached through a client that set no context.
        if (format == ThinkingPayloadFormat.DeepSeek || IsDeepSeekEndpoint(message.Request?.Uri))
        {
            EnsureDeepSeekReasoningContent(message);
            return;
        }

        if (context?.Thinking is not { Effort: not ReasoningEffort.None } thinking)
            return;

        // Anthropic: thinking handled by AnthropicThinkingChatClient decorator;
        // None: always-on / inline <think> vendors take no thinking parameters
        if (format is ThinkingPayloadFormat.Anthropic or ThinkingPayloadFormat.None)
            return;

        var request = message.Request;
        if (request?.Content == null)
            return;

        try
        {
            using var bodyStream = new MemoryStream();
            request.Content.WriteTo(bodyStream, default);
            var json = Encoding.UTF8.GetString(bodyStream.ToArray());

            using var doc = JsonDocument.Parse(json);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();

                // Copy existing properties, skipping ones we'll replace
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.NameEquals("extra_body") || prop.NameEquals("reasoning") || prop.NameEquals("reasoning_effort") ||
                        prop.NameEquals("enable_thinking") || prop.NameEquals("thinking_budget"))
                        continue;
                    prop.WriteTo(writer);
                }

                var model = doc.RootElement.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String
                    ? modelElement.GetString()
                    : null;

                // Route to provider-specific injection
                switch (format)
                {
                    case ThinkingPayloadFormat.Gemini:
                        InjectGeminiThinking(writer, thinking, model);
                        break;
                    case ThinkingPayloadFormat.Qwen:
                        InjectQwenThinking(writer, thinking);
                        break;
                    default:
                        InjectOpenAIThinking(writer, thinking);
                        break;
                }

                writer.WriteEndObject();
            }

            request.Content = BinaryContent.Create(new BinaryData(ms.ToArray()));
        }
        catch
        {
            // Fail-open: skip thinking injection on malformed body
        }
    }

    /// <summary>
    /// OpenAI Chat Completions injection: { "reasoning_effort": "high" }.
    /// The nested { "reasoning": { "effort": ... } } object belongs to the Responses API, which this client does not call.
    /// </summary>
    private static void InjectOpenAIThinking(Utf8JsonWriter writer, ThinkingOptions thinking)
    {
        writer.WriteString("reasoning_effort", MapOpenAIEffort(thinking.Effort));
    }

    /// <summary>
    /// Gemini injection: { "extra_body": { "google": { "thinking_config": { ... } } } }.
    /// Gemini 2.5 takes <c>thinking_budget</c>, later families take <c>thinking_level</c>; only one is sent.
    /// </summary>
    private static void InjectGeminiThinking(Utf8JsonWriter writer, ThinkingOptions thinking, string? model)
    {
        writer.WritePropertyName("extra_body");
        writer.WriteStartObject();
        writer.WritePropertyName("google");
        writer.WriteStartObject();
        writer.WritePropertyName("thinking_config");
        writer.WriteStartObject();
        writer.WriteBoolean("include_thoughts", true);
        if (UsesGeminiThinkingBudget(model))
            writer.WriteNumber("thinking_budget", thinking.BudgetTokens ?? MapGeminiBudget(thinking.Effort));
        else
            writer.WriteString("thinking_level", MapGeminiEffort(thinking.Effort));
        writer.WriteEndObject(); // thinking_config
        writer.WriteEndObject(); // google
        writer.WriteEndObject(); // extra_body
    }

    private static bool UsesGeminiThinkingBudget(string? model)
        => model?.Contains("gemini-2.", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Qwen injection: { "enable_thinking": true, "thinking_budget": N }
    /// </summary>
    private static void InjectQwenThinking(Utf8JsonWriter writer, ThinkingOptions thinking)
    {
        writer.WriteBoolean("enable_thinking", true);
        var budget = thinking.BudgetTokens ?? MapQwenBudget(thinking.Effort);
        writer.WriteNumber("thinking_budget", budget);
    }

    private static string MapOpenAIEffort(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => "low",
        ReasoningEffort.Medium => "medium",
        ReasoningEffort.High => "high",
        ReasoningEffort.Max => "high",
        _ => "none"
    };

    /// <summary>Gemini 3 起的 thinking_level：与 Gemini 文档里 reasoning_effort → thinking_level 的对应一致（Pro 不收 minimal）</summary>
    private static string MapGeminiEffort(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => "low",
        ReasoningEffort.Medium => "medium",
        ReasoningEffort.High => "high",
        ReasoningEffort.Max => "high",
        _ => "low"
    };

    /// <summary>Gemini 2.5 的 thinking_budget：与 Gemini 文档里 reasoning_effort → thinking_budget 的对应一致</summary>
    private static int MapGeminiBudget(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => 1024,
        ReasoningEffort.Medium => 8192,
        _ => 24576
    };

    private static int MapQwenBudget(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => 512,
        ReasoningEffort.Medium => 4096,
        ReasoningEffort.High => 16384,
        ReasoningEffort.Max => 32768,
        _ => 0
    };

    private static bool IsDeepSeekEndpoint(Uri? uri)
        => uri?.Host.Contains("deepseek", StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>
    /// DeepSeek thinking mode requires all assistant messages in the conversation history
    /// to include the "reasoning_content" field, even for tool-call turns where MEAI
    /// strips reasoning from the outgoing message. Patch the request JSON to add an empty
    /// reasoning_content to any assistant message that is missing the field.
    /// See: https://api-docs.deepseek.com/guides/thinking_mode#tool-calls
    /// </summary>
    private static void EnsureDeepSeekReasoningContent(PipelineMessage message)
    {
        var request = message.Request;
        if (request.Content == null) return;

        try
        {
            using var bodyStream = new MemoryStream();
            request.Content.WriteTo(bodyStream, default);
            var json = Encoding.UTF8.GetString(bodyStream.ToArray());

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Only process if messages array exists
            if (!root.TryGetProperty("messages", out var messagesEl)) return;

            // Check if any assistant message is missing reasoning_content
            var needsPatch = false;
            foreach (var msg in messagesEl.EnumerateArray())
            {
                if (msg.TryGetProperty("role", out var role) &&
                    role.GetString() == "assistant" &&
                    !msg.TryGetProperty("reasoning_content", out _))
                {
                    needsPatch = true;
                    break;
                }
            }

            if (!needsPatch) return;

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                foreach (var prop in root.EnumerateObject())
                {
                    if (!prop.NameEquals("messages"))
                    {
                        prop.WriteTo(writer);
                        continue;
                    }

                    writer.WritePropertyName("messages");
                    writer.WriteStartArray();
                    foreach (var msgEl in prop.Value.EnumerateArray())
                    {
                        var isAssistant = msgEl.TryGetProperty("role", out var roleEl) &&
                                          roleEl.GetString() == "assistant";
                        var hasReasoning = msgEl.TryGetProperty("reasoning_content", out _);

                        writer.WriteStartObject();
                        foreach (var msgProp in msgEl.EnumerateObject())
                            msgProp.WriteTo(writer);

                        // Inject empty reasoning_content so DeepSeek accepts the message
                        if (isAssistant && !hasReasoning)
                            writer.WriteString("reasoning_content", "");

                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }

            request.Content = BinaryContent.Create(new BinaryData(ms.ToArray()));
        }
        catch
        {
            // Fail-open: if patching fails, let the original request through
        }
    }

    /// <summary>
    /// Wrap the response ContentStream with a reasoning-extracting stream
    /// when a ChannelWriter is available from the decorator.
    /// </summary>
    private static void WrapResponseForReasoningExtraction(PipelineMessage message)
    {
        var writer = ReasoningAwareChatClientDecorator.ReasoningChannelWriter.Value;
        if (writer == null) return;

        var response = message.Response;
        if (response == null) return;

        var contentStream = response.ContentStream;
        if (contentStream == null) return;

        // Check for streaming response (SSE)
        var contentType = response.Headers.FirstOrDefault(h =>
            string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)).Value;

        if (contentType == null
            || (!contentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase)
                && !contentType.Contains("stream", StringComparison.OrdinalIgnoreCase)))
            return;

        // Replace ContentStream with the extracting wrapper
        response.ContentStream = new ReasoningExtractingStream(contentStream, writer);
    }
}

/// <summary>
/// Context passed from ThinkingMiddleware/ReasoningAwareChatClientDecorator to ThinkingRequestPolicy.
/// </summary>
/// <remarks>
/// 管线内由 <c>ThinkingMiddleware</c> 写入，<b>关闭也写</b>（<see cref="ThinkingOptions.Effort"/> 为 None）：
/// 「没有上下文」只表示调用不经过管线（IAiUtility、评审、摘要），那时装饰器才回退到提供商配置。
/// </remarks>
public sealed class ThinkingRequestContext
{
    /// <summary>推理配置；<see cref="ThinkingOptions.Effort"/> 为 None 表示显式关闭</summary>
    public required ThinkingOptions Thinking { get; init; }

    /// <summary>Provider 条目名（日志与无 <see cref="Format"/> 时的回退路由）</summary>
    public string? ProviderName { get; init; }

    /// <summary>请求体注入形态；由绑定了提供商与模型的装饰器写入，为 null 时按 <see cref="ProviderName"/> 识别</summary>
    public ThinkingPayloadFormat? Format { get; init; }
}
