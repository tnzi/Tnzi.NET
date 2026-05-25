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

        var usage = await GetUsageAsync(threadId, ct);

        if (quota.MaxCommandCount > 0 && usage.CommandCount >= quota.MaxCommandCount)
        {
            return ThreadQuotaCheckResult.Deny(
                $"Thread command count limit reached ({usage.CommandCount}/{quota.MaxCommandCount})");
        }

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

        return ThreadQuotaCheckResult.Allow(
            remainingCommands: quota.MaxCommandCount > 0 ? quota.MaxCommandCount - usage.CommandCount : long.MaxValue,
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
            // Three independent atomic counters; TTL is refreshed on every increment so
            // an actively-used thread keeps its counters alive through the window.
            await _cache.IncrementAsync(KeyCommandCount(threadId), 1, ttl, ct);

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
