namespace Tnzi.AI.Middleware;

/// <summary>
/// Truncates excess 'task' tool calls in a single model response to enforce sub-agent concurrency limit.
/// </summary>
public class SubAgentLimitMiddleware : IAiMiddleware
{
    private const int MinLimit = 2;
    private const int MaxLimit = 4;
    private const string TaskToolName = "task";

    private readonly IOptionsMonitor<SubAgentOptions> _options;
    private readonly ILogger<SubAgentLimitMiddleware> _logger;

    public int Order => AiMiddlewareOrders.SubAgentLimit;

    public SubAgentLimitMiddleware(IOptionsMonitor<SubAgentOptions> options, ILogger<SubAgentLimitMiddleware> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(
        AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        if (!_options.CurrentValue.Enabled)
            return await next(context, cancellationToken);

        TruncateTaskCalls(context);
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context, AiStreamingMiddlewareDelegate next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_options.CurrentValue.Enabled)
        {
            await foreach (var chunk in next(context, cancellationToken))
                yield return chunk;
            yield break;
        }

        TruncateTaskCalls(context);
        await foreach (var chunk in next(context, cancellationToken))
            yield return chunk;
    }

    private void TruncateTaskCalls(AiMiddlewareContext context)
    {
        var maxConcurrent = Math.Clamp(_options.CurrentValue.MaxConcurrentSubAgents, MinLimit, MaxLimit);

        // Find last assistant message
        for (var i = context.Messages.Count - 1; i >= 0; i--)
        {
            var msg = context.Messages[i];
            if (msg.Role != ChatRole.Assistant) continue;

            var taskCallIndices = msg.Contents
                .Select((c, idx) => (Content: c, Index: idx))
                .Where(x => x.Content is FunctionCallContent fc && fc.Name.Equals(TaskToolName, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Index)
                .ToList();

            if (taskCallIndices.Count <= maxConcurrent) return;

            _logger.LogWarning("Truncating {Count} task calls to {Limit} (max concurrent sub-agents)",
                taskCallIndices.Count, maxConcurrent);

            // Keep first maxConcurrent task calls, remove excess
            var indicesToRemove = taskCallIndices.Skip(maxConcurrent).OrderByDescending(x => x).ToList();
            var newContents = msg.Contents.ToList();
            foreach (var idx in indicesToRemove)
                newContents.RemoveAt(idx);

            context.Messages[i] = new ChatMessage(msg.Role, newContents);
            return;
        }
    }
}
