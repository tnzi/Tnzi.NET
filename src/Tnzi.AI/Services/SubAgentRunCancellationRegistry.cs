namespace Tnzi.AI.Services;

/// <summary>
/// Thread-safe singleton implementation of <see cref="ISubAgentRunCancellationRegistry"/>.
/// Uses a <see cref="ConcurrentDictionary{TKey,TValue}"/> to map run IDs → CTS (+ optional timeout timer + recorded reason).
/// </summary>
public sealed class SubAgentRunCancellationRegistry : ISubAgentRunCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, Entry> _registry = new();

    /// <inheritdoc />
    public void Register(Guid runId, CancellationTokenSource cts, TimeSpan? timeout = null)
    {
        Check.NotNull(cts);

        var entry = new Entry(cts);
        // TryAdd - silently ignore if already registered (edge-case: duplicate runId).
        if (!_registry.TryAdd(runId, entry))
        {
            return;
        }

        if (timeout.HasValue)
        {
            // 超时经注册表自己触发，而不是 cts.CancelAfter：只有走 Cancel(runId, kind, reason) 这一条路，
            // 收尾时才分得清「到点了」与「被 kill 了」。
            var reason = $"Timed out after {timeout.Value.TotalSeconds:0.###}s";
            entry.TimeoutTimer = new Timer(_ => Cancel(runId, RunCancellationKind.TimedOut, reason),
                null, timeout.Value, Timeout.InfiniteTimeSpan);
        }
    }

    /// <inheritdoc />
    public CancellationTokenSource? Unregister(Guid runId)
    {
        if (!_registry.TryRemove(runId, out var entry))
        {
            return null;
        }

        entry.TimeoutTimer?.Dispose();
        return entry.Cts;
    }

    /// <inheritdoc />
    public bool TryCancel(Guid runId, string reason)
    {
        Check.NotNullOrWhiteSpace(reason);
        return Cancel(runId, RunCancellationKind.Killed, reason);
    }

    /// <inheritdoc />
    public RunCancellation? GetCancellation(Guid runId)
        => _registry.TryGetValue(runId, out var entry) ? entry.Cancellation : null;

    private bool Cancel(Guid runId, RunCancellationKind kind, string reason)
    {
        if (!_registry.TryGetValue(runId, out var entry))
        {
            return false;
        }

        // 原因先落、令牌后触发：收尾代码一拿到 OperationCanceledException 就能读到原因，不存在窗口。
        // 第一个原因为准（kill 与超时同时到达时不互相覆盖）。
        entry.RecordCancellation(new RunCancellation(kind, reason));

        try
        {
            entry.Cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // CTS was already disposed - remove stale entry
            if (_registry.TryRemove(runId, out var stale))
            {
                stale.TimeoutTimer?.Dispose();
            }

            return false;
        }
    }

    private sealed class Entry
    {
        private RunCancellation? _cancellation;

        public Entry(CancellationTokenSource cts)
        {
            Cts = cts;
        }

        public CancellationTokenSource Cts { get; }
        public Timer? TimeoutTimer { get; set; }
        public RunCancellation? Cancellation => Volatile.Read(ref _cancellation);

        public void RecordCancellation(RunCancellation cancellation)
            => Interlocked.CompareExchange(ref _cancellation, cancellation, null);
    }
}
