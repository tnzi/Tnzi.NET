namespace Tnzi.AI.Middleware;

/// <summary>
/// 推理模式中间件 - 根据 per-request 或 provider 配置自动管理推理模式。
/// <para>
/// 职责：
/// 1. 确定有效推理强度 (request override > provider config)
/// 2. 检测模型推理能力（支持/不支持/always-on）
/// 3. 设置 AsyncLocal 上下文供 ThinkingRequestPolicy 注入 provider 特定参数
/// </para>
/// <para>
/// 自动模型切换由 AgentRuntime 在 Agent 解析前完成（因为 chatClient 在解析时绑定模型）。
/// 此中间件仅负责 AsyncLocal 上下文管理。
/// </para>
/// </summary>
public class ThinkingMiddleware : IAiMiddleware
{
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly ILogger<ThinkingMiddleware> _logger;

    public int Order => AiMiddlewareOrders.Thinking;

    public ThinkingMiddleware(IOptionsMonitor<AIOptions> options, ILogger<ThinkingMiddleware> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        SetupThinkingContext(context);
        try
        {
            return await next(context, cancellationToken);
        }
        finally
        {
            CleanupThinkingContext();
        }
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context,
        AiStreamingMiddlewareDelegate next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        SetupThinkingContext(context);
        try
        {
            await foreach (var chunk in next(context, cancellationToken).WithCancellation(cancellationToken))
            {
                yield return chunk;
            }
        }
        finally
        {
            CleanupThinkingContext();
        }
    }

    /// <summary>
    /// 设置推理上下文：确定有效 Effort、检查模型能力、设置 AsyncLocal
    /// </summary>
    private void SetupThinkingContext(AiMiddlewareContext context)
    {
        var providerName = context.Agent.Provider;
        var providerOptions = GetProviderOptions(providerName);
        if (providerOptions == null) return;

        // 1. 确定有效推理强度: request override > provider config
        var effort = context.Request.ReasoningEffort
                     ?? providerOptions.Thinking?.Effort
                     ?? ReasoningEffort.None;

        if (effort == ReasoningEffort.None) return;

        // 2. 检查当前模型的推理能力
        var currentModel = context.Agent.Model;
        var supportsReasoning = ModelCapabilities.SupportsReasoning(currentModel);
        var isAlwaysOn = ModelCapabilities.IsAlwaysOnReasoning(currentModel);

        if (!supportsReasoning && !isAlwaysOn)
        {
            // 当前模型不支持推理 - 静默跳过（自动模型切换在 AgentRuntime 预解析阶段完成）
            _logger.LogDebug(
                "Model '{CurrentModel}' does not support reasoning for provider '{Provider}', skipping thinking injection",
                currentModel, providerName);
            return;
        }

        // 3. 确定 BudgetTokens: 显式设置 > 自动分配 (80% of MaxTokens)
        var budgetTokens = providerOptions.Thinking?.BudgetTokens;

        if (budgetTokens == null && providerOptions.MaxTokens.HasValue && !isAlwaysOn)
        {
            // 自动分配: Anthropic 等模型需要 budget_tokens，auto = 80% of MaxTokens
            budgetTokens = (int)(providerOptions.MaxTokens.Value * 0.8);
            _logger.LogDebug("Auto-allocated thinking budget: {Budget} tokens (80% of MaxTokens {Max})",
                budgetTokens, providerOptions.MaxTokens.Value);
        }

        // 4. 构建 ThinkingOptions 并设置 AsyncLocal
        var thinkingOptions = new ThinkingOptions
        {
            Effort = effort,
            BudgetTokens = budgetTokens
        };

        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = thinkingOptions,
            ProviderName = providerName
        };

        _logger.LogDebug("Thinking enabled: Effort={Effort}, Provider={Provider}, Model={Model}",
            effort, providerName, currentModel);
    }

    private static void CleanupThinkingContext()
    {
        ThinkingRequestPolicy.RequestContext.Value = null;
    }

    private ProviderOptions? GetProviderOptions(string providerName)
    {
        var aiOptions = _options.CurrentValue;
        return aiOptions.Providers.TryGetValue(providerName, out var opts) ? opts : null;
    }
}
