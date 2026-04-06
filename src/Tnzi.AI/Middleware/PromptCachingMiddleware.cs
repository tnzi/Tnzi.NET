namespace Tnzi.AI.Middleware;

/// <summary>
/// Prompt Caching 中间件 — 为支持缓存的提供商注入缓存控制标记
/// <para>
/// Anthropic: 在 system message 上注入 cache_control: {"type": "ephemeral"} 断点。
/// 启用 CacheStaticDynamicBoundary 后，注入两个断点：
///   1. 静态内容（Agent Instructions）— 跨请求缓存命中率高
///   2. 动态内容（Memory + Context + Skills 注入的最后一条系统消息）— 会话内缓存命中率高
/// </para>
/// <para>
/// 启用 SnapshotToolSchemas 后，首次调用快照工具列表，后续复用以保持 prompt 稳定。
/// </para>
/// <para>
/// OpenAI: 服务端自动缓存，此中间件仅标记 context.Properties 用于指标追踪
/// Gemini: 在 ChatOptions.AdditionalProperties 中注入 google.cached_content 配置
/// </para>
/// </summary>
public class PromptCachingMiddleware : IAiMiddleware
{
    /// <summary>
    /// 已知的动态内容 XML 标签前缀 — 由 ContextInjectionMiddleware 注入的系统消息
    /// </summary>
    private static readonly string[] DynamicContentPrefixes =
    [
        "<soul>", "<user_profile>", "<memory>", "<context>",
        "<skill_system>", "<available-deferred-tools>",
        "<clarification_system>", "<sub_agent_orchestration>",
        "<citations>", "[Conversation summary:"
    ];

    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly ILogger<PromptCachingMiddleware> _logger;

    /// <summary>
    /// 工具 schema 快照缓存 — 首次调用时快照，后续复用以防止配置变更导致缓存失效
    /// </summary>
    private volatile IList<AITool>? _cachedToolSchemas;
    private readonly object _snapshotLock = new();

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
        if (context.ShouldSkipMiddleware)
            return await next(context, cancellationToken);

        ApplyCacheMarkers(context);
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (context.ShouldSkipMiddleware)
        {
            await foreach (var chunk in next(context, cancellationToken))
                yield return chunk;
            yield break;
        }

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

        // OAuth token 检测：Anthropic 4-block cache limit 下 OAuth 场景自动禁用
        if (cachingOptions.DisableOnOAuthToken && context.Properties.ContainsKey("OAuthTokenDetected"))
        {
            _logger.LogDebug("Prompt caching disabled due to OAuth token");
            return;
        }

        // 工具 schema 快照缓存
        if (cachingOptions.SnapshotToolSchemas)
        {
            ApplyToolSchemaSnapshot(context);
        }

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
    /// 首次调用时快照工具列表，后续复用以保持 prompt 中工具 schema 稳定
    /// </summary>
    private void ApplyToolSchemaSnapshot(AiMiddlewareContext context)
    {
        if (_cachedToolSchemas != null)
        {
            // 使用快照替换当前工具列表
            context.AdditionalTools = new List<AITool>(_cachedToolSchemas);
            _logger.LogDebug("Applied cached tool schema snapshot ({Count} tools)", _cachedToolSchemas.Count);
            return;
        }

        lock (_snapshotLock)
        {
            // double-check
            if (_cachedToolSchemas != null)
            {
                context.AdditionalTools = new List<AITool>(_cachedToolSchemas);
                return;
            }

            // 快照当前工具列表（创建副本，防止后续修改影响缓存）
            _cachedToolSchemas = context.AdditionalTools.ToList();
            _logger.LogDebug("Created tool schema snapshot ({Count} tools)", _cachedToolSchemas.Count);
        }
    }

    /// <summary>
    /// 为 Anthropic provider 注入 cache_control 断点到消息的 AdditionalProperties
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
    /// 第一断点 — 静态内容（Agent Instructions 系统消息），跨请求稳定
    /// 第二断点 — 动态内容（最后一条上下文注入的系统消息），会话内稳定
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

        var breakpointCount = 0;

        // Breakpoint 1: 静态内容（Instructions）
        if (staticMessage != null)
        {
            SetCacheBreakpoint(staticMessage);
            breakpointCount++;
            _logger.LogDebug("Applied static cache breakpoint on Instructions system message");
        }

        // Breakpoint 2: 动态内容（最后一条上下文注入的系统消息）
        if (lastDynamicMessage != null)
        {
            SetCacheBreakpoint(lastDynamicMessage);
            breakpointCount++;
            _logger.LogDebug("Applied dynamic cache breakpoint on context-injected system message");
        }

        // 如果没有动态消息但有静态消息，且启用了 CacheFirstNMessages，回退到历史消息缓存
        if (lastDynamicMessage == null && options.CacheFirstNMessages > 0)
        {
            ApplyHistoryMessageBreakpoints(messages, options);
        }

        // Tier 3: 标记最后一个工具定义用于缓存
        if (options.CacheToolDefinitions && context.AdditionalTools.Count > 0)
        {
            context.Properties["CacheLastToolDefinition"] = true;
            _logger.LogDebug("Marked last tool definition for Anthropic cache_control");
        }

        context.Properties["PromptCachingBreakpoints"] = breakpointCount;
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

        // Tier 1: 缓存系统提示
        if (options.CacheSystemPrompt)
        {
            var lastSystemMsg = messages.LastOrDefault(m => m.Role == ChatRole.System);
            if (lastSystemMsg != null)
            {
                SetCacheBreakpoint(lastSystemMsg);
                _logger.LogDebug("Applied Anthropic cache_control to system message");
            }
        }

        // Tier 2: 历史消息缓存
        ApplyHistoryMessageBreakpoints(messages, options);

        // Tier 3: 标记最后一个工具定义用于缓存
        if (options.CacheToolDefinitions && context.AdditionalTools.Count > 0)
        {
            context.Properties["CacheLastToolDefinition"] = true;
            _logger.LogDebug("Marked last tool definition for Anthropic cache_control");
        }
    }

    /// <summary>
    /// 历史消息缓存：CacheFirstNMessages + CacheRecentUserMessages
    /// </summary>
    private void ApplyHistoryMessageBreakpoints(List<ChatMessage> messages, PromptCachingOptions options)
    {
        // Tier 2a: 缓存前 N 条历史消息（在最后一条上设置断点）
        if (options.CacheFirstNMessages > 0)
        {
            var userMessages = messages.Where(m => m.Role == ChatRole.User || m.Role == ChatRole.Assistant).ToList();
            if (userMessages.Count > 0)
            {
                var cacheUpTo = Math.Min(options.CacheFirstNMessages, userMessages.Count);
                var lastCachedMsg = userMessages[cacheUpTo - 1];
                SetCacheBreakpoint(lastCachedMsg);
                _logger.LogDebug("Applied Anthropic cache_control to message {Index} of {Total}", cacheUpTo, userMessages.Count);
            }
        }

        // Tier 2b: 缓存最近 N 条用户消息
        if (options.CacheRecentUserMessages > 0)
        {
            var userMessages = messages.Where(m => m.Role == ChatRole.User).ToList();
            if (userMessages.Count > 0)
            {
                var startIdx = Math.Max(0, userMessages.Count - options.CacheRecentUserMessages);
                for (var i = startIdx; i < userMessages.Count; i++)
                {
                    SetCacheBreakpoint(userMessages[i]);
                }
                _logger.LogDebug("Applied Anthropic cache_control to {Count} recent user messages",
                    Math.Min(options.CacheRecentUserMessages, userMessages.Count));
            }
        }
    }

    /// <summary>
    /// 在 ChatMessage 上设置 Anthropic cache_control 断点
    /// </summary>
    private static void SetCacheBreakpoint(ChatMessage message)
    {
        // Anthropic SDK 通过 AdditionalProperties 传递 cache_control
        message.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        message.AdditionalProperties["cache_control"] = new Dictionary<string, string> { ["type"] = "ephemeral" };
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
