namespace Tnzi.AI.Sandbox.Quota;

/// <summary>
/// Accumulated resource usage for a single AI thread's sandbox activity.
/// Drives <see cref="IThreadResourceQuota.CheckAsync"/> decisions.
/// </summary>
public class ThreadQuotaUsage
{
    /// <summary>Owning thread.</summary>
    public Guid ThreadId { get; init; }

    /// <summary>
    /// Total non-rejected <c>bash</c> invocations admitted in the current window.
    /// Counts every command that passed the quota pre-flight (reserved a slot),
    /// including ones that later exit non-zero - only quota/blacklist-denied
    /// commands are excluded.
    /// </summary>
    public long CommandCount { get; init; }

    /// <summary>Sum of <see cref="System.Diagnostics.Stopwatch.ElapsedMilliseconds"/> across all <c>bash</c> calls.</summary>
    public long TotalDurationMs { get; init; }

    /// <summary>Combined stdout + stderr byte count across all <c>bash</c> calls.</summary>
    public long TotalOutputBytes { get; init; }
}

/// <summary>
/// Outcome of a <see cref="IThreadResourceQuota.CheckAsync"/> probe - communicates
/// whether the next sandbox command is permitted, and how much room is left.
/// </summary>
public class ThreadQuotaCheckResult
{
    /// <summary><c>true</c> when none of the thread quotas has been reached.</summary>
    public bool IsAllowed { get; init; }

    /// <summary>Human-readable explanation when <see cref="IsAllowed"/> is false.</summary>
    public string? Reason { get; init; }

    /// <summary>Commands remaining before the count cap fires; <see cref="long.MaxValue"/> when uncapped.</summary>
    public long RemainingCommands { get; init; }

    /// <summary>Milliseconds remaining before the cumulative duration cap fires; <see cref="long.MaxValue"/> when uncapped.</summary>
    public long RemainingDurationMs { get; init; }

    /// <summary>Output bytes remaining before the byte cap fires; <see cref="long.MaxValue"/> when uncapped.</summary>
    public long RemainingOutputBytes { get; init; }

    public static ThreadQuotaCheckResult Allow(long remainingCommands, long remainingDurationMs, long remainingOutputBytes)
        => new()
        {
            IsAllowed = true,
            RemainingCommands = remainingCommands,
            RemainingDurationMs = remainingDurationMs,
            RemainingOutputBytes = remainingOutputBytes
        };

    public static ThreadQuotaCheckResult Deny(string reason)
        => new()
        {
            IsAllowed = false,
            Reason = reason,
            RemainingCommands = 0,
            RemainingDurationMs = 0,
            RemainingOutputBytes = 0
        };
}
