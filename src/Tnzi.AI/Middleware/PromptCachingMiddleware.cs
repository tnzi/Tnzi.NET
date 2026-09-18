using Anthropic.Models.Messages;

namespace Tnzi.AI.Middleware;

/// <summary>
/// Prompt Caching 中间件 - 为支持缓存的提供商注入缓存控制标记
/// <para>
/// Anthropic: 在 system message 上注入 cache_control: {"type": "ephemeral"} 断点。
/// 启用 CacheStaticDynamicBoundary 后，注入两个断点：
///   1. 静态内容（非动态注入的系统消息）- 跨请求缓存命中率高
///   2. 动态内容（Memory + Context + Skills 注入的最后一条系统消息）- 会话内缓存命中率高
/// </para>
/// <para>
/// ★ 断点写在<b>内容块</b>上（<c>AIContent.AdditionalProperties["anthropic:cache_control"]</c>，经 SDK 的
/// <c>WithCacheControl</c> 写入），工具定义断点写在 <c>AITool.AdditionalProperties["CacheControl"]</c> 上 ——
/// 这两处是 Anthropic MEAI 适配器唯一会读的位置。此前写在 <c>ChatMessage.AdditionalProperties["cache_control"]</c>
/// 上的标记从未到过线路：适配器逐块转换时只看内容块，从不读消息级属性，于是日志说「已应用」而
/// 出站请求里一个 cache_control 都没有、CachedInputTokens 恒 0。
/// </para>
/// <para>
/// OpenAI: 服务端自动缓存，此中间件仅标记 context.Properties 用于指标追踪
/// Gemini: 在 ChatOptions.AdditionalProperties 中注入 google.cached_content 配置
/// </para>
/// </summary>
public class PromptCachingMiddleware : IAiMiddleware
{
    /// <summary>
    /// 已知的动态内容 XML 标签前缀 - 由 ContextInjectionMiddleware 注入的系统消息
    /// </summary>
    private static readonly string[] DynamicContentPrefixes =
    [
        "<soul>", "<user_profile>", "<memory>", "<context>",
        "<skill_system>", "<available-deferred-tools>",
        "<clarification_system>", "<sub_agent_orchestration>",
        "<citations>", "[Conversation summary:"
    ];

    /// <summary>
    /// Anthropic 每个请求最多接受 4 个带 cache_control 的块，多一个整条请求 400。
    /// 断点按价值封顶：工具定义 &gt; 静态系统 &gt; 动态系统 &gt; 前 N 条历史 &gt; 最近用户消息（最新的优先保留）。
    /// </summary>
    internal const int MaxAnthropicBreakpoints = 4;

    /// <summary>SDK 的 <c>WithCacheControl</c> 写在内容块上的键（用于识别已标记的块，同一块不占两个名额）。</summary>
    private const string CacheControlKey = "anthropic:cache_control";

    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly ILogger<PromptCachingMiddleware> _logger;

    /// <summary>
    /// Order 460: 在 SkillConstraint(450) 之后，可见全部消息和工具；在 UsageLogging(500) 之前
    /// </summary>
    public int Order => AiMiddlewareOrders.PromptCaching;

    public PromptCachingMiddleware(IOptionsMonitor<AIOptions> options, ILogger<PromptCachingMiddleware> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        ApplyCacheMarkers(context);
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ApplyCacheMarkers(context);
        await foreach (var chunk in next(context, cancellationToken).WithCancellation(cancellationToken))
        {
            yield return chunk;
        }
    }

    private void ApplyCacheMarkers(AiMiddlewareContext context)
    {
        var providerName = context.EffectiveProvider ?? context.Agent.Provider;
        var cachingOptions = ResolveCachingOptions(providerName);
        if (cachingOptions is not { Enabled: true }) return;

        // 标记上下文：启用了 prompt caching（供 UsageLoggingMiddleware 追踪指标）
        context.Properties["PromptCachingEnabled"] = true;

        // Anthropic 风格: 注入 cache_control 断点
        if (IsAnthropicProvider(providerName))
        {
            ApplyAnthropicCacheBreakpoints(context, cachingOptions);
        }
        // OpenAI: 自动缓存，无需客户端操作
        // Gemini: 需要 CachedContent API（暂不支持，需要原生 SDK）
    }

    /// <summary>
    /// 为 Anthropic provider 注入 cache_control 断点到消息的内容块
    /// </summary>
    private void ApplyAnthropicCacheBreakpoints(AiMiddlewareContext context, PromptCachingOptions options)
    {
        var messages = context.Messages;
        if (messages.Count == 0) return;

        // 优先使用 static/dynamic 双断点策略
        if (options.CacheStaticDynamicBoundary)
        {
            ApplyStaticDynamicBreakpoints(context, options);
            return;
        }

        // 兼容旧逻辑：单层 system prompt 缓存
        ApplyLegacyBreakpoints(context, options);
    }

    /// <summary>
    /// Static/Dynamic 双断点策略：
    /// 第一断点 - 静态内容（Agent Instructions 系统消息），跨请求稳定
    /// 第二断点 - 动态内容（最后一条上下文注入的系统消息），会话内稳定
    /// </summary>
    private void ApplyStaticDynamicBreakpoints(AiMiddlewareContext context, PromptCachingOptions options)
    {
        var messages = context.Messages;
        var systemMessages = messages.Where(m => m.Role == ChatRole.System).ToList();
        if (systemMessages.Count == 0) return;

        ChatMessage? staticMessage = null;
        ChatMessage? lastDynamicMessage = null;

        foreach (var msg in systemMessages)
        {
            if (IsDynamicContent(msg))
            {
                lastDynamicMessage = msg;
            }
            else
            {
                // 非动态内容的系统消息视为静态（Instructions、response_style 等）
                staticMessage = msg;
            }
        }

        var budget = new BreakpointBudget(MaxAnthropicBreakpoints);

        // Tier 3 先占名额：工具定义在缓存前缀里排在 system 之前，是最长最稳的那段
        if (options.CacheToolDefinitions)
        {
            ApplyToolDefinitionBreakpoint(context, budget);
        }

        // Breakpoint 1: 静态内容（Instructions）
        if (staticMessage != null && SetCacheBreakpoint(staticMessage, budget))
        {
            _logger.LogDebug("Applied static cache breakpoint on Instructions system message");
        }

        // Breakpoint 2: 动态内容（最后一条上下文注入的系统消息）
        if (lastDynamicMessage != null && SetCacheBreakpoint(lastDynamicMessage, budget))
        {
            _logger.LogDebug("Applied dynamic cache breakpoint on context-injected system message");
        }

        // 如果没有动态消息但有静态消息，且启用了 CacheFirstNMessages，回退到历史消息缓存
        if (lastDynamicMessage == null && options.CacheFirstNMessages > 0)
        {
            ApplyHistoryMessageBreakpoints(messages, options, budget);
        }

        FinishBudget(context, budget);
    }

    /// <summary>
    /// 记下实际写到线路上的断点数；有配置项因名额不够被跳过时记一条日志 —— 那是配置要求的东西没有全部生效。
    /// </summary>
    private void FinishBudget(AiMiddlewareContext context, BreakpointBudget budget)
    {
        context.Properties["PromptCachingBreakpoints"] = budget.Applied;
        if (budget.Skipped > 0)
        {
            _logger.LogInformation(
                "Prompt caching requested {Requested} cache breakpoints but Anthropic allows at most {Max} per request; " +
                "{Skipped} lower-value breakpoint(s) were not applied. Lower CacheRecentUserMessages / CacheFirstNMessages to silence this.",
                budget.Applied + budget.Skipped, MaxAnthropicBreakpoints, budget.Skipped);
        }
    }

    /// <summary>
    /// 判断系统消息是否为动态注入的内容（Memory/Context/Skills/Soul/UserProfile 等）
    /// </summary>
    private static bool IsDynamicContent(ChatMessage message)
    {
        var text = message.Text;
        if (string.IsNullOrEmpty(text)) return false;

        foreach (var prefix in DynamicContentPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 兼容旧逻辑的单层缓存策略
    /// </summary>
    private void ApplyLegacyBreakpoints(AiMiddlewareContext context, PromptCachingOptions options)
    {
        var messages = context.Messages;
        var budget = new BreakpointBudget(MaxAnthropicBreakpoints);

        // Tier 3 先占名额（同 static/dynamic 策略）
        if (options.CacheToolDefinitions)
        {
            ApplyToolDefinitionBreakpoint(context, budget);
        }

        // Tier 1: 缓存系统提示
        if (options.CacheSystemPrompt)
        {
            var lastSystemMsg = messages.LastOrDefault(m => m.Role == ChatRole.System);
            if (lastSystemMsg != null && SetCacheBreakpoint(lastSystemMsg, budget))
            {
                _logger.LogDebug("Applied Anthropic cache_control to system message");
            }
        }

        // Tier 2: 历史消息缓存
        ApplyHistoryMessageBreakpoints(messages, options, budget);

        FinishBudget(context, budget);
    }

    /// <summary>
    /// 把中间件注入的最后一个工具定义标为缓存断点。
    /// ★ 只覆盖 <see cref="AiMiddlewareContext.AdditionalTools"/>：Agent 自带的工具在执行器里，
    /// 中间件看不到，而 Anthropic 按「tools → system → messages」顺序算前缀，
    /// 中间件注入的工具排在 Agent 工具之后，断点落在最后一个上即可覆盖整段工具定义。
    /// </summary>
    private void ApplyToolDefinitionBreakpoint(AiMiddlewareContext context, BreakpointBudget budget)
    {
        if (context.AdditionalTools.Count == 0) return;
        if (!context.AdditionalTools.Any(t => t is AIFunction)) return;
        if (!budget.TryTake()) return;
        if (MarkLastToolDefinitionForCaching(context.AdditionalTools))
        {
            _logger.LogDebug("Applied Anthropic cache_control to the last tool definition");
        }
    }

    /// <summary>
    /// 历史消息缓存：CacheFirstNMessages + CacheRecentUserMessages
    /// </summary>
    private void ApplyHistoryMessageBreakpoints(List<ChatMessage> messages, PromptCachingOptions options, BreakpointBudget budget)
    {
        // Tier 2a: 缓存前 N 条历史消息（在最后一条上设置断点）
        if (options.CacheFirstNMessages > 0)
        {
            var userMessages = messages.Where(m => m.Role == ChatRole.User || m.Role == ChatRole.Assistant).ToList();
            if (userMessages.Count > 0)
            {
                var cacheUpTo = Math.Min(options.CacheFirstNMessages, userMessages.Count);
                var lastCachedMsg = userMessages[cacheUpTo - 1];
                if (SetCacheBreakpoint(lastCachedMsg, budget))
                {
                    _logger.LogDebug("Applied Anthropic cache_control to message {Index} of {Total}", cacheUpTo, userMessages.Count);
                }
            }
        }

        // Tier 2b: 缓存最近 N 条用户消息 —— 从最新往回标，名额不够时丢掉的是较旧的那些
        if (options.CacheRecentUserMessages > 0)
        {
            var userMessages = messages.Where(m => m.Role == ChatRole.User).ToList();
            if (userMessages.Count > 0)
            {
                var applied = 0;
                var startIdx = Math.Max(0, userMessages.Count - options.CacheRecentUserMessages);
                for (var i = userMessages.Count - 1; i >= startIdx; i--)
                {
                    if (SetCacheBreakpoint(userMessages[i], budget)) applied++;
                }
                _logger.LogDebug("Applied Anthropic cache_control to {Count} recent user messages", applied);
            }
        }
    }

    /// <summary>
    /// 计 Anthropic 断点名额：<see cref="Applied"/> 是真的写到线路上的块数，<see cref="Skipped"/> 是配置要求却没名额的。
    /// </summary>
    private sealed class BreakpointBudget(int capacity)
    {
        private readonly HashSet<AIContent> _counted = new(ReferenceEqualityComparer.Instance);

        public int Applied { get; private set; }
        public int Skipped { get; private set; }

        public bool TryTake()
        {
            if (Applied >= capacity)
            {
                Skipped++;
                return false;
            }
            Applied++;
            return true;
        }

        /// <summary>同一个块只占一个名额（前 N 条与最近 N 条可能落在同一条消息上）。</summary>
        public bool TryTake(AIContent block)
        {
            if (_counted.Contains(block)) return true;
            if (!TryTake()) return false;
            _counted.Add(block);
            return true;
        }
    }

    /// <summary>
    /// 在 ChatMessage 的最后一个可缓存内容块上设置断点（默认 5 分钟 TTL），占一个名额。
    /// 该块已经带断点时只计数不重写；消息没有可缓存的块，或名额已用尽时不写任何东西。
    /// </summary>
    /// <returns>该块最终是否带着断点。</returns>
    private static bool SetCacheBreakpoint(ChatMessage message, BreakpointBudget budget)
    {
        var target = FindCacheableContent(message);
        if (target is null) return false;
        if (!budget.TryTake(target)) return false;
        if (target.AdditionalProperties?.ContainsKey(CacheControlKey) != true)
        {
            target.WithCacheControl(ttl: null);
        }
        return true;
    }

    /// <summary>消息里最后一个能带 cache_control 的块（thinking / redacted thinking 不支持，SDK 会忽略）。</summary>
    private static AIContent? FindCacheableContent(ChatMessage message)
    {
        for (var i = message.Contents.Count - 1; i >= 0; i--)
        {
            if (message.Contents[i] is not TextReasoningContent) return message.Contents[i];
        }
        return null;
    }

    /// <summary>
    /// 在 ChatMessage 的最后一个可缓存内容块上设置 Anthropic cache_control 断点。
    /// </summary>
    /// <remarks>
    /// 断点按块生效：「到此块为止的全部前缀」被缓存，故落在消息的最后一块上。
    /// thinking / redacted thinking 块不支持 cache_control（SDK 会忽略），跳过它们；
    /// 消息没有可缓存的块时不写任何东西。
    /// </remarks>
    /// <returns>是否真的写下了断点。</returns>
    internal static bool SetCacheBreakpoint(ChatMessage message, Ttl? ttl)
    {
        Check.NotNull(message);
        var target = FindCacheableContent(message);
        if (target is null) return false;
        target.WithCacheControl(ttl);
        return true;
    }

    /// <summary>
    /// 把工具列表里最后一个 <see cref="AIFunction"/> 换成带 cache_control 的包装，
    /// SDK 适配器从 <c>AITool.AdditionalProperties["CacheControl"]</c> 读出后写到 tool 定义上。
    /// </summary>
    /// <returns>是否真的标记了一个工具。</returns>
    internal static bool MarkLastToolDefinitionForCaching(IList<AITool> tools)
    {
        Check.NotNull(tools);
        for (var i = tools.Count - 1; i >= 0; i--)
        {
            if (tools[i] is not AIFunction function) continue;
            if (function is CacheControlAIFunction) return true;
            tools[i] = new CacheControlAIFunction(function, new CacheControlEphemeral());
            return true;
        }
        return false;
    }

    /// <summary>
    /// 在既有 <see cref="AIFunction"/> 之上追加一个 AdditionalProperties 条目的透明包装：
    /// <see cref="AITool.AdditionalProperties"/> 是只读的，已建好的函数改不了它。
    /// </summary>
    private sealed class CacheControlAIFunction : DelegatingAIFunction
    {
        private readonly IReadOnlyDictionary<string, object?> _additionalProperties;

        public CacheControlAIFunction(AIFunction inner, CacheControlEphemeral cacheControl) : base(inner)
        {
            var merged = new Dictionary<string, object?>(inner.AdditionalProperties)
            {
                [nameof(Tool.CacheControl)] = Check.NotNull(cacheControl)
            };
            _additionalProperties = merged;
        }

        public override IReadOnlyDictionary<string, object?> AdditionalProperties => _additionalProperties;
    }

    private PromptCachingOptions? ResolveCachingOptions(string providerName)
    {
        if (_options.CurrentValue.Providers.TryGetValue(providerName, out var providerOptions))
        {
            return providerOptions.PromptCaching;
        }
        return null;
    }

    private static bool IsAnthropicProvider(string providerName)
    {
        return providerName.Contains("anthropic", StringComparison.OrdinalIgnoreCase)
            || providerName.Contains("claude", StringComparison.OrdinalIgnoreCase);
    }
}
