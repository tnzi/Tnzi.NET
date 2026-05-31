namespace Tnzi.AI.Services;

/// <summary>
/// Thread-safe singleton implementation of <see cref="ISubAgentRunCancellationRegistry"/>.
/// Uses a <see cref="ConcurrentDictionary{TKey,TValue}"/> to map run IDs → CTS.
/// </summary>
public sealed class SubAgentRunCancellationRegistry : ISubAgentRunCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _registry = new();

    /// <inheritdoc />
    public void Register(Guid runId, CancellationTokenSource cts)
    {
        Check.NotNull(cts);
        // TryAdd — silently ignore if already registered (edge-case: duplicate runId).
        _registry.TryAdd(runId, cts);
    }

    /// <inheritdoc />
    public CancellationTokenSource? Unregister(Guid runId)
    {
        _registry.TryRemove(runId, out var cts);
        return cts;
    }

    /// <inheritdoc />
    public bool TryCancel(Guid runId)
    {
        if (!_registry.TryGetValue(runId, out var cts))
            return false;

        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // CTS was already disposed — remove stale entry
            _registry.TryRemove(runId, out _);
            return false;
        }
    }
}
