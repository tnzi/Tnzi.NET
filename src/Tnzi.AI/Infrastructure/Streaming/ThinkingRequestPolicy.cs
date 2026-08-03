
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
    /// Routes to different injection formats based on ProviderName.
    /// </summary>
    private static void InjectThinkingParams(PipelineMessage message)
    {
        var context = RequestContext.Value;
        var providerName = context?.ProviderName;

        // DeepSeek: always-on reasoning, no body injection needed.
        // BUT multi-turn tool calls require all assistant messages to carry reasoning_content.
        // MEAI doesn't serialize reasoning content into outgoing messages, so we patch it here.
        // Detect via explicit provider name OR by request URL - the latter handles always-on
        // reasoning models (e.g., deepseek-reasoner) selected via think-model routing where
        // no explicit ThinkingOptions are configured and RequestContext may be null.
        if (IsDeepSeekProvider(providerName) || IsDeepSeekEndpoint(message.Request?.Uri))
        {
            EnsureDeepSeekReasoningContent(message);
            return;
        }

        if (context?.Thinking is not { Effort: not ReasoningEffort.None } thinking)
            return;

        // Anthropic: thinking handled by AnthropicThinkingChatClient decorator
        if (IsAnthropicProvider(providerName))
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
                    if (prop.NameEquals("extra_body") || prop.NameEquals("reasoning") ||
                        prop.NameEquals("enable_thinking") || prop.NameEquals("thinking_budget"))
                        continue;
                    prop.WriteTo(writer);
                }

                // Route to provider-specific injection
                if (IsGeminiProvider(providerName))
                    InjectGeminiThinking(writer, thinking);
                else if (IsQwenProvider(providerName))
                    InjectQwenThinking(writer, thinking);
                else
                    InjectOpenAIThinking(writer, thinking);

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
    /// OpenAI reasoning injection: { "reasoning": { "effort": "high", "summary": "auto" } }
    /// </summary>
    private static void InjectOpenAIThinking(Utf8JsonWriter writer, ThinkingOptions thinking)
    {
        writer.WritePropertyName("reasoning");
        writer.WriteStartObject();
        writer.WriteString("effort", MapOpenAIEffort(thinking.Effort));
        writer.WriteString("summary", "auto");
        writer.WriteEndObject();
    }

    /// <summary>
    /// Gemini injection: { "extra_body": { "google": { "thinking_config": { ... } } } }
    /// </summary>
    private static void InjectGeminiThinking(Utf8JsonWriter writer, ThinkingOptions thinking)
    {
        writer.WritePropertyName("extra_body");
        writer.WriteStartObject();
        writer.WritePropertyName("google");
        writer.WriteStartObject();
        writer.WritePropertyName("thinking_config");
        writer.WriteStartObject();
        writer.WriteBoolean("include_thoughts", true);
        writer.WriteString("thinking_level", MapGeminiEffort(thinking.Effort));
        if (thinking.BudgetTokens.HasValue)
            writer.WriteNumber("thinking_budget", thinking.BudgetTokens.Value);
        writer.WriteEndObject(); // thinking_config
        writer.WriteEndObject(); // google
        writer.WriteEndObject(); // extra_body
    }

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

    private static string MapGeminiEffort(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => "minimal",
        ReasoningEffort.Medium => "low",
        ReasoningEffort.High => "medium",
        ReasoningEffort.Max => "high",
        _ => "none"
    };

    private static int MapQwenBudget(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => 512,
        ReasoningEffort.Medium => 4096,
        ReasoningEffort.High => 16384,
        ReasoningEffort.Max => 32768,
        _ => 0
    };

    private static bool IsDeepSeekProvider(string? providerName)
        => string.Equals(providerName, "DeepSeek", StringComparison.OrdinalIgnoreCase);

    private static bool IsDeepSeekEndpoint(Uri? uri)
        => uri?.Host.Contains("deepseek", StringComparison.OrdinalIgnoreCase) ?? false;

    private static bool IsGeminiProvider(string? providerName)
        => string.Equals(providerName, "Gemini", StringComparison.OrdinalIgnoreCase)
           || string.Equals(providerName, "Google", StringComparison.OrdinalIgnoreCase);

    private static bool IsQwenProvider(string? providerName)
        => string.Equals(providerName, "Qwen", StringComparison.OrdinalIgnoreCase)
           || string.Equals(providerName, "DashScope", StringComparison.OrdinalIgnoreCase)
           || string.Equals(providerName, "Alibaba", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnthropicProvider(string? providerName)
        => string.Equals(providerName, "Anthropic", StringComparison.OrdinalIgnoreCase);

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
public sealed class ThinkingRequestContext
{
    /// <summary>推理配置</summary>
    public required ThinkingOptions Thinking { get; init; }

    /// <summary>Provider 名称，用于路由到不同的注入格式</summary>
    public string? ProviderName { get; init; }
}
