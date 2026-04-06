namespace Tnzi.AI.Middleware;

/// <summary>
/// Removes deferred tools from the available tool list before sending to LLM.
/// Deferred tools remain executable but are hidden from model binding.
/// Discoverable via tool_search tool.
/// </summary>
public class DeferredToolFilterMiddleware : IAiMiddleware
{
    private readonly ILogger<DeferredToolFilterMiddleware> _logger;

    public int Order => AiMiddlewareOrders.DeferredToolFilter;

    public DeferredToolFilterMiddleware(ILogger<DeferredToolFilterMiddleware> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(
        AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        if (!context.ShouldSkipMiddleware)
            FilterDeferredTools(context);
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context, AiStreamingMiddlewareDelegate next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!context.ShouldSkipMiddleware)
            FilterDeferredTools(context);
        await foreach (var chunk in next(context, cancellationToken))
            yield return chunk;
    }

    private void FilterDeferredTools(AiMiddlewareContext context)
    {
        var registry = ToolDeferredRegistry.Current;
        if (registry is null || registry.Count == 0) return;

        var deferredNames = new HashSet<string>(
            registry.GetDeferredEntries().Select(e => e.Name), StringComparer.OrdinalIgnoreCase);

        var before = context.AdditionalTools.Count;
        context.AdditionalTools.RemoveAll(t =>
            t.Name is not null && deferredNames.Contains(t.Name));

        var removed = before - context.AdditionalTools.Count;
        if (removed > 0)
            _logger.LogDebug("Filtered {Count} deferred tools from model binding", removed);
    }
}
