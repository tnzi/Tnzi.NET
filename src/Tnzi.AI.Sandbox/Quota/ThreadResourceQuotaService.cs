using Tnzi.Caching;

namespace Tnzi.AI.Sandbox.Quota;

/// <summary>
/// Default <see cref="IThreadResourceQuota"/> implementation backed by three
/// independent <see cref="ICache"/> atomic counters per thread. Failure to
/// read or write the cache is logged but never thrown — observability for
/// quotas must never interrupt the agent.
/// </summary>
public sealed class ThreadResourceQuotaService : IThreadResourceQuota
{
    private readonly ICache _cache;
    private readonly IOptions<SandboxModuleOptions> _options;
    private readonly ILogger<ThreadResourceQuotaService> _logger;

    public ThreadResourceQuotaService(
        ICache cache,
        IOptions<SandboxModuleOptions> options,
        ILogger<ThreadResourceQuotaService> logger)
    {
        _cache = Check.NotNull(cache);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    public async Task<ThreadQuotaCheckResult> CheckAsync(Guid threadId, CancellationToken ct = default)
    {
        var quota = _options.Value.ThreadQuota;
        if (!quota.Enabled)
            return ThreadQuotaCheckResult.Allow(long.MaxValue, long.MaxValue, long.MaxValue);

        // Duration / output are soft (approximate) caps — enforced from the
        // previously-recorded usage, which can momentarily lag a parallel command.
        var usage = await GetUsageAsync(threadId, ct);

        if (quota.MaxTotalDurationMs > 0 && usage.TotalDurationMs >= quota.MaxTotalDurationMs)
        {
            return ThreadQuotaCheckResult.Deny(
                $"Thread cumulative duration limit reached ({usage.TotalDurationMs}/{quota.MaxTotalDurationMs}ms)");
        }

        if (quota.MaxTotalOutputBytes > 0 && usage.TotalOutputBytes >= quota.MaxTotalOutputBytes)
        {
            return ThreadQuotaCheckResult.Deny(
                $"Thread cumulative output limit reached ({usage.TotalOutputBytes}/{quota.MaxTotalOutputBytes} bytes)");
        }

        // Command count is a HARD cap: reserve a slot with a single atomic
        // increment so two parallel CheckAsync calls cannot both pass at the
        // boundary (closes the previous Check→Record TOCTOU window). The
        // post-increment value IS the decision point; if it exceeds the cap we
        // roll the reservation back and deny.
        if (quota.MaxCommandCount > 0)
        {
            long reserved;
            try
            {
                reserved = await _cache.IncrementAsync(KeyCommandCount(threadId), 1, quota.WindowDuration, ct);
            }
            catch (Exception ex)
            {
                // Cache failure must never block the agent — fall through to allow.
                _logger.LogWarning(ex, "Failed to reserve thread command quota for thread {ThreadId}", threadId);
                return ThreadQuotaCheckResult.Allow(long.MaxValue,
                    remainingDurationMs: quota.MaxTotalDurationMs > 0 ? quota.MaxTotalDurationMs - usage.TotalDurationMs : long.MaxValue,
                    remainingOutputBytes: quota.MaxTotalOutputBytes > 0 ? quota.MaxTotalOutputBytes - usage.TotalOutputBytes : long.MaxValue);
            }

            if (reserved > quota.MaxCommandCount)
            {
                // Over the cap — give the reserved slot back so a later window
                // reset / different thread is not penalised, then deny.
                try { await _cache.IncrementAsync(KeyCommandCount(threadId), -1, quota.WindowDuration, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to roll back thread command quota for thread {ThreadId}", threadId); }

                return ThreadQuotaCheckResult.Deny(
                    $"Thread command count limit reached ({quota.MaxCommandCount}/{quota.MaxCommandCount})");
            }

            return ThreadQuotaCheckResult.Allow(
                remainingCommands: quota.MaxCommandCount - reserved,
                remainingDurationMs: quota.MaxTotalDurationMs > 0 ? quota.MaxTotalDurationMs - usage.TotalDurationMs : long.MaxValue,
                remainingOutputBytes: quota.MaxTotalOutputBytes > 0 ? quota.MaxTotalOutputBytes - usage.TotalOutputBytes : long.MaxValue);
        }

        return ThreadQuotaCheckResult.Allow(
            remainingCommands: long.MaxValue,
            remainingDurationMs: quota.MaxTotalDurationMs > 0 ? quota.MaxTotalDurationMs - usage.TotalDurationMs : long.MaxValue,
            remainingOutputBytes: quota.MaxTotalOutputBytes > 0 ? quota.MaxTotalOutputBytes - usage.TotalOutputBytes : long.MaxValue);
    }

    public async Task RecordExecutionAsync(Guid threadId, long durationMs, long outputBytes, CancellationToken ct = default)
    {
        var quota = _options.Value.ThreadQuota;
        if (!quota.Enabled) return;

        var ttl = quota.WindowDuration;

        try
        {
            // Command count is already reserved up-front in CheckAsync (hard cap),
            // so it is intentionally NOT incremented here — doing so would
            // double-count. Duration and output bytes are soft caps recorded
            // after the fact.
            if (durationMs > 0)
                await _cache.IncrementAsync(KeyDurationMs(threadId), durationMs, ttl, ct);

            if (outputBytes > 0)
                await _cache.IncrementAsync(KeyOutputBytes(threadId), outputBytes, ttl, ct);
        }
        catch (Exception ex)
        {
            // Silent catch — quota accounting must not break the agent flow.
            _logger.LogWarning(ex, "Failed to record thread quota usage for thread {ThreadId}", threadId);
        }
    }

    public async Task<ThreadQuotaUsage> GetUsageAsync(Guid threadId, CancellationToken ct = default)
    {
        try
        {
            var keys = new[] { KeyCommandCount(threadId), KeyDurationMs(threadId), KeyOutputBytes(threadId) };
            var batch = await _cache.GetManyAsync<long>(keys, ct);

            return new ThreadQuotaUsage
            {
                ThreadId = threadId,
                CommandCount = batch.TryGetValue(keys[0], out var c) ? c : 0,
                TotalDurationMs = batch.TryGetValue(keys[1], out var d) ? d : 0,
                TotalOutputBytes = batch.TryGetValue(keys[2], out var b) ? b : 0
            };
        }
        catch (Exception ex)
        {
            // Silent fall-through on cache failure: report empty usage rather than blocking the agent.
            _logger.LogWarning(ex, "Failed to read thread quota usage for thread {ThreadId}", threadId);
            return new ThreadQuotaUsage { ThreadId = threadId };
        }
    }

    public async Task ResetAsync(Guid threadId, CancellationToken ct = default)
    {
        try
        {
            await _cache.RemoveAsync(KeyCommandCount(threadId), ct);
            await _cache.RemoveAsync(KeyDurationMs(threadId), ct);
            await _cache.RemoveAsync(KeyOutputBytes(threadId), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reset thread quota for thread {ThreadId}", threadId);
        }
    }

    // Cache key layout — three independent atomic counters per thread.
    // Using a stable string prefix lets ICache.RemoveByPrefixAsync wipe an
    // entire thread cleanly if needed (operations dashboards, tests).
    private static string KeyCommandCount(Guid t) => $"sandbox:thread-quota:{t:N}:cmd";
    private static string KeyDurationMs(Guid t) => $"sandbox:thread-quota:{t:N}:ms";
    private static string KeyOutputBytes(Guid t) => $"sandbox:thread-quota:{t:N}:bytes";
}
