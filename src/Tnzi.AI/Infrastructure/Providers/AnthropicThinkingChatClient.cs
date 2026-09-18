using Anthropic.Models.Messages;
using MeaiReasoningEffort = Microsoft.Extensions.AI.ReasoningEffort;

namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// Anthropic thinking 装饰器 - 把框架的 <see cref="ThinkingOptions"/> 翻译成 SDK 适配器真正读取的通道。
/// Anthropic SDK 原生返回 TextReasoningContent，无需 ReasoningAwareChatClientDecorator。
/// </summary>
/// <remarks>
/// <para>
/// Anthropic 的 MEAI 适配器只从两处取 thinking：<see cref="ChatOptions.Reasoning"/>（按 Effort 映射固定预算
/// 1024 / 8192 / 16384 / 32768，并自动抬高 max_tokens）与 <see cref="ChatOptions.RawRepresentationFactory"/>
/// 返回的 <see cref="MessageCreateParams"/>（<c>Thinking</c> 已设时优先）。它**从不读**
/// <see cref="ChatOptions.AdditionalProperties"/> —— 此前写在 <c>AdditionalProperties["thinking"]</c> 上的注入
/// 在任一已固定的 SDK 版本上都没有到过线路，请求照常成功而 Claude 从不进入 extended thinking。
/// </para>
/// <para>
/// 默认按 Effort 走 <see cref="ChatOptions.Reasoning"/>；显式 <see cref="ThinkingOptions.BudgetTokens"/> 无法经
/// Effort 表达，改走 RawRepresentationFactory —— 那条路上适配器把 <c>Model</c> / <c>MaxTokens</c> 也交给调用方，
/// 故这里要自己补齐（模型取自适配器的 <see cref="ChatClientMetadata.DefaultModelId"/>），并保证
/// max_tokens 大于 budget_tokens（API 硬约束）。
/// </para>
/// </remarks>
public sealed class AnthropicThinkingChatClient : DelegatingChatClient
{
    /// <summary>与 SDK 适配器的默认值一致：没配 MaxOutputTokens 时，max_tokens = budget + 此值。</summary>
    internal const int DefaultMaxOutputTokens = 1024;

    /// <summary>Anthropic 要求的最小 thinking 预算。</summary>
    internal const int MinBudgetTokens = 1024;

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

        // Clone options to avoid mutating the original
        var newOptions = options != null ? CloneOptions(options) : new ChatOptions();

        if (thinking.BudgetTokens is { } explicitBudget)
        {
            ApplyExplicitBudget(newOptions, explicitBudget);
            return newOptions;
        }

        newOptions.Reasoning = new ReasoningOptions { Effort = MapEffort(thinking.Effort) };
        return newOptions;
    }

    /// <summary>
    /// 显式预算走 RawRepresentationFactory。适配器在这条路上不再从 ChatOptions 补 Model / MaxTokens，
    /// 也不再按预算抬高 max_tokens，两者都要在这里定下来。
    /// </summary>
    private static void ApplyExplicitBudget(ChatOptions options, int budgetTokens)
    {
        var budget = Math.Max(budgetTokens, MinBudgetTokens);
        var maxTokens = options.MaxOutputTokens;
        if (maxTokens.HasValue && maxTokens.Value <= budget)
        {
            // 与 SDK 的 Effort 路径同一取舍：调用方定了输出上限就把预算压进去，而不是悄悄改上限
            budget = maxTokens.Value - 1;
        }
        if (budget < MinBudgetTokens)
        {
            // 上限太小装不下最小预算：不发 thinking（与适配器自身的处理一致），保留其余选项
            return;
        }

        var effectiveMaxTokens = maxTokens ?? budget + DefaultMaxOutputTokens;
        var capturedBudget = budget;
        options.RawRepresentationFactory = client =>
        {
            var modelId = options.ModelId
                ?? client.GetService<ChatClientMetadata>()?.DefaultModelId
                ?? throw new InvalidOperationException(
                    "Model ID must be specified either in ChatOptions or as the default for the Anthropic client.");
            return new MessageCreateParams
            {
                Model = modelId,
                MaxTokens = effectiveMaxTokens,
                Messages = [],
                Thinking = new ThinkingConfigParam(new ThinkingConfigEnabled(capturedBudget))
            };
        };
    }

    /// <summary>
    /// 框架四档 → MEAI 四档，一一对应；SDK 再把它们映射成 1024 / 8192 / 16384 / 32768 的预算。
    /// </summary>
    internal static MeaiReasoningEffort MapEffort(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => MeaiReasoningEffort.Low,
        ReasoningEffort.Medium => MeaiReasoningEffort.Medium,
        ReasoningEffort.High => MeaiReasoningEffort.High,
        ReasoningEffort.Max => MeaiReasoningEffort.ExtraHigh,
        _ => MeaiReasoningEffort.None
    };

    private static ChatOptions CloneOptions(ChatOptions source)
    {
        var clone = new ChatOptions
        {
            ModelId = source.ModelId,
            Instructions = source.Instructions,
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
            Reasoning = source.Reasoning,
            RawRepresentationFactory = source.RawRepresentationFactory,
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
