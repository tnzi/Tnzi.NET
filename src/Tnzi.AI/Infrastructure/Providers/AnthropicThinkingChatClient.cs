using Tnzi.AI.Infrastructure.Streaming;

namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// Anthropic thinking 装饰器 — 在 ChatOptions.AdditionalProperties 中注入 thinking 参数。
/// Anthropic SDK 原生返回 TextReasoningContent，无需 ReasoningAwareChatClientDecorator。
/// </summary>
public sealed class AnthropicThinkingChatClient : DelegatingChatClient
{
    public AnthropicThinkingChatClient(IChatClient innerClient) : base(innerClient) { }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options = InjectThinkingIfNeeded(options);
        return base.GetResponseAsync(chatMessages, options, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options = InjectThinkingIfNeeded(options);
        return base.GetStreamingResponseAsync(chatMessages, options, cancellationToken);
    }

    private static ChatOptions? InjectThinkingIfNeeded(ChatOptions? options)
    {
        var context = ThinkingRequestPolicy.RequestContext.Value;
        if (context?.Thinking is not { Effort: not ReasoningEffort.None } thinking)
            return options;

        var budgetTokens = thinking.BudgetTokens ?? MapBudgetTokens(thinking.Effort);

        // Clone options to avoid mutating the original
        var newOptions = options != null ? CloneOptions(options) : new ChatOptions();
        newOptions.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        newOptions.AdditionalProperties["thinking"] = new Dictionary<string, object>
        {
            ["type"] = "enabled",
            ["budget_tokens"] = budgetTokens
        };

        return newOptions;
    }

    private static int MapBudgetTokens(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => 1024,
        ReasoningEffort.Medium => 8192,
        ReasoningEffort.High => 16384,
        ReasoningEffort.Max => 32768,
        _ => 0
    };

    private static ChatOptions CloneOptions(ChatOptions source)
    {
        var clone = new ChatOptions
        {
            ModelId = source.ModelId,
            Temperature = source.Temperature,
            MaxOutputTokens = source.MaxOutputTokens,
            TopP = source.TopP,
            TopK = source.TopK,
            StopSequences = source.StopSequences,
            FrequencyPenalty = source.FrequencyPenalty,
            PresencePenalty = source.PresencePenalty,
            Seed = source.Seed,
            ResponseFormat = source.ResponseFormat,
            ConversationId = source.ConversationId,
        };

        if (source.Tools is { Count: > 0 })
        {
            clone.Tools = [.. source.Tools];
        }

        if (source.ToolMode is not null)
        {
            clone.ToolMode = source.ToolMode;
        }

        if (source.AdditionalProperties is { Count: > 0 })
        {
            clone.AdditionalProperties = new AdditionalPropertiesDictionary(source.AdditionalProperties);
        }

        return clone;
    }
}
