namespace Tnzi.AI.Middleware;

/// <summary>
/// Detects repeated tool call patterns using a sliding window hash tracking.
/// Injects warning messages at threshold and forces stop at hard limit.
/// </summary>
public class LoopDetectionMiddleware : IAiMiddleware
{
    private const string WarningMsg = "[LOOP DETECTED] You are repeating the same tool calls. Try a different approach or respond to the user directly.";
    private const string HardStopMsg = "[FORCED STOP] Repeated tool calls exceeded the limit. You must respond without using tools.";

    private readonly IOptions<LoopDetectionOptions> _options;
    private readonly ILogger<LoopDetectionMiddleware> _logger;

    // Per-thread tracking: threadKey → list of hashes (using LinkedList for LRU)
    private readonly Dictionary<string, List<string>> _history = new();
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, HashSet<string>> _warned = new();
    private readonly object _lock = new();

    public int Order => AiMiddlewareOrders.LoopDetection;

    public LoopDetectionMiddleware(IOptions<LoopDetectionOptions> options, ILogger<LoopDetectionMiddleware> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(
        AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        if (context.ShouldSkipMiddleware || !_options.Value.Enabled)
            return await next(context, cancellationToken);

        var result = await next(context, cancellationToken);
        CheckAndApply(context);
        return result;
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context, AiStreamingMiddlewareDelegate next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (context.ShouldSkipMiddleware || !_options.Value.Enabled)
        {
            await foreach (var chunk in next(context, cancellationToken))
                yield return chunk;
            yield break;
        }

        await foreach (var chunk in next(context, cancellationToken))
            yield return chunk;

        CheckAndApply(context);
    }

    private void CheckAndApply(AiMiddlewareContext context)
    {
        var toolCalls = ExtractLastToolCalls(context.Messages);
        if (toolCalls.Count == 0) return;

        var hash = HashToolCalls(toolCalls);
        var threadKey = GetThreadKey(context);
        var opts = _options.Value;

        lock (_lock)
        {
            if (!_history.TryGetValue(threadKey, out var hashes))
            {
                hashes = [];
                _history[threadKey] = hashes;
                _lruOrder.AddLast(threadKey);
            }
            else
            {
                // Move to end for LRU
                _lruOrder.Remove(threadKey);
                _lruOrder.AddLast(threadKey);
            }

            hashes.Add(hash);

            // Keep only window_size
            if (hashes.Count > opts.WindowSize)
                hashes.RemoveRange(0, hashes.Count - opts.WindowSize);

            var count = hashes.Count(h => h == hash);

            if (count >= opts.HardLimit)
            {
                _logger.LogError("Loop hard limit reached for thread {ThreadKey}, hash {Hash}, count {Count}",
                    threadKey, hash, count);
                context.Messages.Add(new ChatMessage(ChatRole.User, HardStopMsg));
            }
            else if (count >= opts.WarnThreshold)
            {
                _warned.TryGetValue(threadKey, out var warnedHashes);
                if (warnedHashes is null || !warnedHashes.Contains(hash))
                {
                    _logger.LogWarning("Loop detected for thread {ThreadKey}, hash {Hash}, count {Count}",
                        threadKey, hash, count);
                    context.Messages.Add(new ChatMessage(ChatRole.User, WarningMsg));
                    (warnedHashes ??= []).Add(hash);
                    _warned[threadKey] = warnedHashes;
                }
            }

            // LRU eviction
            while (_history.Count > opts.MaxTrackedThreads && _lruOrder.First is not null)
            {
                var oldest = _lruOrder.First.Value;
                _lruOrder.RemoveFirst();
                _history.Remove(oldest);
                _warned.Remove(oldest);
            }
        }
    }

    private static List<(string Name, string Args)> ExtractLastToolCalls(List<ChatMessage> messages)
    {
        var result = new List<(string, string)>();
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            if (msg.Role != ChatRole.Assistant) continue;

            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent fc)
                    result.Add((fc.Name, JsonSerializer.Serialize(fc.Arguments ?? new Dictionary<string, object?>())));
            }
            break;
        }
        return result;
    }

    internal static string HashToolCalls(List<(string Name, string Args)> toolCalls)
    {
        var sorted = toolCalls.OrderBy(t => t.Name).ThenBy(t => t.Args).ToList();
        var json = JsonSerializer.Serialize(sorted);
        return json.ToMd5()[..12];
    }

    private static string GetThreadKey(AiMiddlewareContext context)
    {
        if (context.Request.Metadata?.TryGetValue("ThreadId", out var tid) == true)
            return tid.ToString() ?? "default";
        return context.Request.ThreadId?.ToString() ?? "default";
    }

    public void Reset(string? threadKey = null)
    {
        lock (_lock)
        {
            if (threadKey is null)
            {
                _history.Clear();
                _lruOrder.Clear();
                _warned.Clear();
            }
            else
            {
                _history.Remove(threadKey);
                _lruOrder.Remove(threadKey);
                _warned.Remove(threadKey);
            }
        }
    }
}
