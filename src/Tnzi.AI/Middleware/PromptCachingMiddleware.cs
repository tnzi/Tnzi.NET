namespace Tnzi.AI.Middleware;

/// <summary>
/// Prompt Caching 中间件 — 为支持缓存的提供商注入缓存控制标记
/// <para>
/// Anthropic: 在 system message 和最后一条工具定义上注入 cache_control: {"type": "ephemeral"}
/// OpenAI: 服务端自动缓存，此中间件仅标记 context.Properties 用于指标追踪
/// Gemini: 在 ChatOptions.AdditionalProperties 中注入 google.cached_content 配置
/// </para>
/// </summary>
public class PromptCachingMiddleware : IAiMiddleware
{
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly ILogger<PromptCachingMiddleware> _logger;

    /// <summary>
    /// Order 75: 在 Quota(100) 之前，Thinking(50) 之后
    /// </summary>
    public int Order => AiMiddlewareOrders.PromptCaching;

    public PromptCachingMiddleware(IOptionsMonitor<AIOptions> options, ILogger<PromptCachingMiddleware> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        if (context.Agent.ExecutionMode == AgentExecutionMode.ExternalCli)
            return await next(context, cancellationToken);

        ApplyCacheMarkers(context);
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (context.Agent.ExecutionMode == AgentExecutionMode.ExternalCli)
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
    /// 为 Anthropic provider 注入 cache_control 断点到消息的 AdditionalProperties
    /// </summary>
    private void ApplyAnthropicCacheBreakpoints(AiMiddlewareContext context, PromptCachingOptions options)
    {
        var messages = context.Messages;
        if (messages.Count == 0) return;

        // 1. 缓存系统提示
        if (options.CacheSystemPrompt)
        {
            var lastSystemMsg = messages.LastOrDefault(m => m.Role == ChatRole.System);
            if (lastSystemMsg != null)
            {
                SetCacheBreakpoint(lastSystemMsg);
                _logger.LogDebug("Applied Anthropic cache_control to system message");
            }
        }

        // 2. 缓存前 N 条历史消息（在最后一条上设置断点）
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
