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
/// 决定「关」时同样写下上下文（Effort = None），而不是留空：留空在下游等于「调用不经过管线」，
/// <see cref="ReasoningAwareChatClientDecorator"/> 会按提供商配置把推理打开，请求显式要求的 None 与
/// 模型不支持推理的判断就被绕过。注入形态由装饰器按它绑定的提供商与模型识别，这里不管。
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

    /// <summary>
    /// 已提示过「要了推理但模型不在能力表里」的提供商 + 模型。中间件按作用域创建，
    /// 记录放在进程级，才能做到每个组合只提示一次。
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> SkippedModels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 记录的组合数上限：模型名可以来自请求（per-request 覆盖），不设上限的话一串随手编的模型名就能让它无限增长。
    /// 到上限之后不再记新组合，只回到 Debug。
    /// </summary>
    private const int MaxSkippedModelsTracked = 1000;

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

        // 1. 确定有效推理强度: request override > provider config
        var effort = context.Request.ReasoningEffort
                     ?? providerOptions?.Thinking?.Effort
                     ?? ReasoningEffort.None;

        if (effort == ReasoningEffort.None)
        {
            SetThinkingOff(providerName);
            return;
        }

        // 2. 检查当前模型的推理能力
        var currentModel = context.Agent.Model;
        var supportsReasoning = ModelCapabilities.SupportsReasoning(currentModel);
        var isAlwaysOn = ModelCapabilities.IsAlwaysOnReasoning(currentModel);

        if (!supportsReasoning && !isAlwaysOn)
        {
            // 当前模型不在推理能力表里 - 跳过（自动模型切换在 AgentRuntime 预解析阶段完成）。
            // 配置或请求明确要了推理却被跳过，每个提供商 + 模型记一条 Information：
            // 能力表是按名字认的，新模型没收录时这是运维唯一看得到的信号
            if (SkippedModels.Count < MaxSkippedModelsTracked && SkippedModels.TryAdd($"{providerName}|{currentModel}", 0))
            {
                _logger.LogInformation(
                    "Reasoning effort {Effort} was requested but model '{CurrentModel}' (provider '{Provider}') is not known to support reasoning; thinking is not requested for it",
                    effort, currentModel, providerName);
            }
            else
            {
                _logger.LogDebug(
                    "Model '{CurrentModel}' does not support reasoning for provider '{Provider}', skipping thinking injection",
                    currentModel, providerName);
            }
            SetThinkingOff(providerName);
            return;
        }

        // 3. 确定 BudgetTokens: 显式设置 > 自动分配 (80% of MaxTokens)
        var budgetTokens = providerOptions?.Thinking?.BudgetTokens;

        if (budgetTokens == null && providerOptions?.MaxTokens is { } maxTokens && !isAlwaysOn)
        {
            // 自动分配: Anthropic 等模型需要 budget_tokens，auto = 80% of MaxTokens
            budgetTokens = (int)(maxTokens * 0.8);
            _logger.LogDebug("Auto-allocated thinking budget: {Budget} tokens (80% of MaxTokens {Max})",
                budgetTokens, maxTokens);
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

    private static void SetThinkingOff(string providerName)
    {
        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.None },
            ProviderName = providerName
        };
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
