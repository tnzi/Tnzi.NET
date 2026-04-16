using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tnzi.DependencyInjection;

namespace Tnzi.AI.Tools.Approval.Sse;

public sealed class InMemoryPendingApprovalStore : IPendingApprovalStore, ISingletonDependency
{
    private readonly ConcurrentDictionary<Guid, PendingEntry> _entries = new();

    private sealed record PendingEntry(
        PendingApprovalRequest Request,
        TaskCompletionSource<ApprovalDecision> Waiter);

    public Task<Guid> RegisterAsync(PendingApprovalRequest request, CancellationToken ct = default)
    {
        var entry = new PendingEntry(
            request,
            new TaskCompletionSource<ApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously));
        _entries[request.Id] = entry;
        return Task.FromResult(request.Id);
    }

    public Task<PendingApprovalRequest?> GetAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_entries.TryGetValue(id, out var e) ? e.Request : null);

    public Task<bool> ResolveAsync(Guid id, ApprovalDecision decision, CancellationToken ct = default)
    {
        if (_entries.TryRemove(id, out var entry))
        {
            entry.Waiter.TrySetResult(decision);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<PendingApprovalRequest>> ListPendingAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PendingApprovalRequest>>(_entries.Values.Select(e => e.Request).ToList());

    /// <summary>
    /// Internal method for handlers to await an approval decision by ID.
    /// Not part of the public interface — handlers obtain this store via DI and call directly.
    /// </summary>
    internal async Task<ApprovalDecision> AwaitDecisionAsync(Guid id, CancellationToken ct = default)
    {
        if (!_entries.TryGetValue(id, out var entry))
            throw new InvalidOperationException($"No waiter for {id}");

        // Ensure the entry is removed when the wait is cancelled, so the dictionary
        // doesn't accumulate abandoned entries. The TCS itself is garbage-collected
        // with the removed entry.
        using var registration = ct.Register(() =>
        {
            _entries.TryRemove(id, out _);
        });

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
}
