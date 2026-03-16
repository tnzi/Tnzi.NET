namespace Tnzi.AI.Middleware;

/// <summary>
/// 上下文注入中间件 — Before only: 注入 Memory/RAG/Skills 上下文
/// </summary>
public class ContextInjectionMiddleware : IAiMiddleware
{
    private readonly CompositeContextProvider _contextProvider;
    private readonly ILogger<ContextInjectionMiddleware> _logger;

    public int Order => 400;

    public ContextInjectionMiddleware(
        CompositeContextProvider contextProvider,
        ILogger<ContextInjectionMiddleware> logger)
    {
        _contextProvider = Check.NotNull(contextProvider);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        // Before: 注入上下文
        await InjectContextAsync(context, cancellationToken);
        return await next(context, cancellationToken);
    }

    /// <summary>
    /// 流式路径 — Before: 注入上下文后再委托给下游
    /// </summary>
    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(AiMiddlewareContext context, AiStreamingMiddlewareDelegate next, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Before: 注入上下文（与非流式路径相同逻辑）
        await InjectContextAsync(context, cancellationToken);

        await foreach (var chunk in next(context, cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// 共享的上下文注入逻辑
    /// </summary>
    private async Task InjectContextAsync(AiMiddlewareContext context, CancellationToken cancellationToken)
    {
        if (_contextProvider.ProviderCount <= 0) return;

        try
        {
            var injection = await _contextProvider.GetContextAsync(context.Messages, cancellationToken);

            if (injection.Messages is { Count: > 0 })
            {
                context.Messages.InsertRange(0, injection.Messages);
                _logger.LogDebug("Injected {Count} context messages", injection.Messages.Count);
            }

            if (injection.Tools is { Count: > 0 })
            {
                context.AdditionalTools.AddRange(injection.Tools);
                _logger.LogDebug("Injected {Count} additional tools", injection.Tools.Count);
            }

            if (injection.Citations is { Count: > 0 })
            {
                context.Citations.AddRange(injection.Citations);
                _logger.LogDebug("Injected {Count} citations", injection.Citations.Count);
            }

            if (injection.ActiveSkills is { Count: > 0 })
            {
                context.Properties["ActiveSkills"] = injection.ActiveSkills;
                _logger.LogDebug("Propagated {Count} active skills to middleware context", injection.ActiveSkills.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Context injection failed, continuing without context");
        }
    }
}
