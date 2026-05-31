namespace Tnzi.AI.Tools.Approval.Sse;

/// <summary>
/// In-memory <see cref="IPendingApprovalStore"/>. Suitable for single-process deployments
/// (or sticky-session multi-instance setups). For horizontally scaled deployments where
/// the SSE stream and the approval callback may land on different nodes, replace with a
/// distributed implementation (e.g. Redis pub/sub).
///
/// TTL handling: every entry has an absolute expiry; a background sweep evicts expired
/// entries and completes their waiters with a fail-closed timeout decision (Approved=false).
/// </summary>
public sealed class InMemoryPendingApprovalStore : IPendingApprovalStore, IDisposable
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, PendingEntry> _entries = new();
    private readonly Timer _sweepTimer;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public InMemoryPendingApprovalStore() : this(TimeProvider.System) { }

    public InMemoryPendingApprovalStore(TimeProvider timeProvider)
    {
        _timeProvider = Check.NotNull(timeProvider);
        _sweepTimer = new Timer(SweepExpired, null, SweepInterval, SweepInterval);
    }

    private sealed record PendingEntry(
        PendingApprovalRequest Request,
        TaskCompletionSource<ApprovalDecision> Waiter,
        DateTimeOffset ExpiresAt);

    public Task<Guid> RegisterAsync(PendingApprovalRequest request, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        Check.NotNull(request);
        if (string.IsNullOrWhiteSpace(request.UserId))
            throw new InvalidOperationException("PendingApprovalRequest.UserId must be non-empty for security reasons.");

        var expiresAt = _timeProvider.GetUtcNow() + (ttl ?? DefaultTtl);
        var entry = new PendingEntry(
            request,
            new TaskCompletionSource<ApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously),
            expiresAt);

        if (!_entries.TryAdd(request.Id, entry))
            throw new InvalidOperationException($"Approval request {request.Id} already registered.");

        return Task.FromResult(request.Id);
    }

    public Task<PendingApprovalRequest?> GetAsync(Guid id, string? currentUserId = null, CancellationToken ct = default)
    {
        if (!_entries.TryGetValue(id, out var e))
            return Task.FromResult<PendingApprovalRequest?>(null);

        if (currentUserId is not null
            && !string.Equals(e.Request.UserId, currentUserId, StringComparison.Ordinal))
        {
            return Task.FromResult<PendingApprovalRequest?>(null);
        }

        return Task.FromResult<PendingApprovalRequest?>(e.Request);
    }

    public Task<ResolveResult> ResolveAsync(
        Guid id,
        ApprovalDecision decision,
        string? currentUserId,
        CancellationToken ct = default)
    {
        Check.NotNull(decision);

        if (!_entries.TryGetValue(id, out var entry))
            return Task.FromResult(ResolveResult.NotFound);

        // Authz: only the request owner may resolve. Null/empty currentUserId is "untrusted caller" — reject.
        if (string.IsNullOrEmpty(currentUserId)
            || !string.Equals(entry.Request.UserId, currentUserId, StringComparison.Ordinal))
        {
            return Task.FromResult(ResolveResult.NotAuthorized);
        }

        if (!_entries.TryRemove(id, out var removed))
            return Task.FromResult(ResolveResult.NotFound);

        removed.Waiter.TrySetResult(decision);
        return Task.FromResult(ResolveResult.Resolved);
    }

    public Task<IReadOnlyList<PendingApprovalRequest>> ListPendingAsync(
        string? currentUserId = null,
        CancellationToken ct = default)
    {
        var query = _entries.Values.Select(e => e.Request);
        if (currentUserId is not null)
            query = query.Where(r => string.Equals(r.UserId, currentUserId, StringComparison.Ordinal));
        return Task.FromResult<IReadOnlyList<PendingApprovalRequest>>(query.ToList());
    }

    public async Task<ApprovalDecision> AwaitDecisionAsync(Guid id, CancellationToken ct = default)
    {
        if (!_entries.TryGetValue(id, out var entry))
            throw new InvalidOperationException($"No waiter registered for {id}");

        using var registration = ct.Register(() => _entries.TryRemove(id, out _));

        try
        {
            return await entry.Waiter.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _entries.TryRemove(id, out _);
            throw;
        }
    }

    public Task ResolveAsSystemAsync(Guid id, ApprovalDecision decision, CancellationToken ct = default)
    {
        Check.NotNull(decision);
        if (_entries.TryRemove(id, out var entry))
        {
            entry.Waiter.TrySetResult(decision);
        }
        return Task.CompletedTask;
    }

    private void SweepExpired(object? state)
    {
        if (_disposed) return;
        var now = _timeProvider.GetUtcNow();
        foreach (var kv in _entries)
        {
            if (kv.Value.ExpiresAt <= now)
            {
                if (_entries.TryRemove(kv.Key, out var expired))
                {
                    expired.Waiter.TrySetResult(new ApprovalDecision(
                        Approved: false,
                        Reason: $"Approval timed out after {(now - expired.Request.CreatedAt).TotalSeconds:F0}s without user response.",
                        DecidedBy: "system"));
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sweepTimer.Dispose();

        foreach (var kv in _entries)
        {
            if (_entries.TryRemove(kv.Key, out var entry))
            {
                entry.Waiter.TrySetResult(new ApprovalDecision(
                    Approved: false,
                    Reason: "Approval store was disposed.",
                    DecidedBy: "system"));
            }
        }
    }
}
