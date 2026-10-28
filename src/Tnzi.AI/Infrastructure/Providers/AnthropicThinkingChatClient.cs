using Anthropic.Models.Messages;
using MeaiReasoningEffort = Microsoft.Extensions.AI.ReasoningEffort;

namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// Anthropic thinking 装饰器 - 把框架的 <see cref="ThinkingOptions"/> 翻译成 SDK 适配器真正读取的通道。
/// Anthropic SDK 原生返回 TextReasoningContent，无需 ReasoningAwareChatClientDecorator。
/// </summary>
/// <remarks>
/// <para>
/// Anthropic 的 MEAI 适配器只从两处取 thinking：<see cref="ChatOptions.Reasoning"/> 与
/// <see cref="ChatOptions.RawRepresentationFactory"/> 返回的 <see cref="MessageCreateParams"/>（<c>Thinking</c>
/// 已设时优先）。它**从不读** <see cref="ChatOptions.AdditionalProperties"/>：此前写在
/// <c>AdditionalProperties["thinking"]</c> 上的注入在任一已固定的 SDK 版本上都没有到过线路，请求照常成功而
/// Claude 从不进入 extended thinking。
/// </para>
/// <para>
/// <see cref="ChatOptions.Reasoning"/> 落到线路上的形态取决于适配器创建时选的 <see cref="AnthropicThinkingMode"/>，
/// 而两种形态各自只被一部分型号接受（见 <see cref="ModelCapabilities.UsesClaudeAdaptiveThinking"/>）：
/// adaptive 是 <c>thinking.type=adaptive</c> + <c>output_config.effort</c>，4.7 起的型号只收这一种；
/// extended 是 <c>thinking.type=enabled</c> + 按 Effort 映射的 <c>budget_tokens</c>（1024 / 8192 / 16384 / 32768，
/// 并自动抬高 max_tokens），4.6 之前的型号只收这一种（4.6 两种都收，取 extended）。本装饰器与适配器由
/// <see cref="AnthropicChatClientProvider.CreateThinkingAwareClient"/> 按型号成对创建，形态一致。
/// </para>
/// <para>
/// 显式 <see cref="ThinkingOptions.BudgetTokens"/> 只在 extended 形态下有意义，那里无法经 Effort 表达，改走
/// RawRepresentationFactory：那条路上适配器把 <c>Model</c> / <c>MaxTokens</c> 也交给调用方，故这里要自己补齐
/// （模型取自适配器的 <see cref="ChatClientMetadata.DefaultModelId"/>），并保证 max_tokens 大于 budget_tokens
/// （API 硬约束）。adaptive 型号拒收 <c>budget_tokens</c>，显式预算在那里按 Effort 走。
/// </para>
/// </remarks>
public sealed class AnthropicThinkingChatClient : DelegatingChatClient
{
    private readonly bool _adaptiveThinking;

    /// <summary>与 SDK 适配器的默认值一致：没配 MaxOutputTokens 时，max_tokens = budget + 此值。</summary>
    internal const int DefaultMaxOutputTokens = 1024;

    /// <summary>Anthropic 要求的最小 thinking 预算。</summary>
    internal const int MinBudgetTokens = 1024;

    /// <param name="innerClient">Anthropic SDK 适配器。</param>
    /// <param name="adaptiveThinking">
    /// 适配器创建时是否选了 <see cref="AnthropicThinkingMode.Adaptive"/>。两者必须一致：本装饰器据此决定
    /// 显式预算能不能发出去。
    /// </param>
    public AnthropicThinkingChatClient(IChatClient innerClient, bool adaptiveThinking) : base(innerClient)
    {
        _adaptiveThinking = adaptiveThinking;
    }

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

    private ChatOptions? InjectThinkingIfNeeded(ChatOptions? options)
    {
        var context = ThinkingRequestPolicy.RequestContext.Value;
        if (context?.Thinking is not { Effort: not ReasoningEffort.None } thinking)
            return options;

        // 推理上下文跟着整次运行走，而单次调用可以换型号（ChatOptions.ModelId，例如上下文摘要用一个更便宜的型号）。
        // 适配器的形态在创建时就定死了：换上的型号不支持推理，或与本客户端不是同一种形态时不注入，否则 API 直接 400
        var model = options?.ModelId ?? InnerClient.GetService<ChatClientMetadata>()?.DefaultModelId;
        if (model is not null
            && (!ModelCapabilities.SupportsReasoning(model) || ModelCapabilities.UsesClaudeAdaptiveThinking(model) != _adaptiveThinking))
        {
            return options;
        }

        // 克隆后再改，不动调用方的 options
        var newOptions = options?.Clone() ?? new ChatOptions();

        if (thinking.BudgetTokens is { } explicitBudget && !_adaptiveThinking)
        {
            ApplyExplicitBudget(newOptions, explicitBudget);
            return newOptions;
        }

        newOptions.Reasoning = new ReasoningOptions
        {
            Effort = MapEffort(thinking.Effort),
            // adaptive 型号默认 display=omitted，thinking 块只剩签名、界面上的推理是空的；要回摘要才看得见。
            // extended 形态下这一项与不设相同（照常返回推理文本）
            Output = ReasoningOutput.Summary
        };
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
    /// 框架四档 → MEAI 四档，一一对应。SDK 再按适配器的形态落地：adaptive 下是 <c>output_config.effort</c>
    /// 的 low / medium / high / xhigh，extended 下是 1024 / 8192 / 16384 / 32768 的预算。
    /// </summary>
    internal static MeaiReasoningEffort MapEffort(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => MeaiReasoningEffort.Low,
        ReasoningEffort.Medium => MeaiReasoningEffort.Medium,
        ReasoningEffort.High => MeaiReasoningEffort.High,
        ReasoningEffort.Max => MeaiReasoningEffort.ExtraHigh,
        _ => MeaiReasoningEffort.None
    };
}
