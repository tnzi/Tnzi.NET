namespace Tnzi.AI.Sandbox.Quota;

/// <summary>
/// Thread-scoped resource quota for sandbox command execution. Caps the
/// number of <c>bash</c> invocations, cumulative execution time, and total
/// output bytes a single AI thread may consume within a configurable rolling
/// window.
/// </summary>
/// <remarks>
/// <para>
/// This quota is complementary to (but independent of) <c>IQuotaService</c>
/// (LLM token quota) and <c>IBudgetService</c> (USD spend). Sandbox compute
/// has its own resource dimensions (CPU seconds, IO bytes) that token-based
/// quotas cannot constrain.
/// </para>
/// <para>
/// Storage is <see cref="Tnzi.Caching.ICache"/>-backed via three independent
/// atomic counters per thread, so the implementation is contention-free even
/// under parallel agent runs that happen to share a thread id.
/// </para>
/// </remarks>
public interface IThreadResourceQuota
{
    /// <summary>
    /// Admission decision for the next sandbox command.
    /// </summary>
    /// <remarks>
    /// The command-count dimension is a <b>hard cap</b>: this method atomically
    /// reserves one command slot (single increment) and uses the post-increment
    /// value as the decision point, rolling the reservation back on denial. This
    /// makes the count cap race-free even when two commands probe concurrently.
    /// The duration and output-byte dimensions are <b>soft (approximate) caps</b>
    /// evaluated against previously-recorded usage, which may briefly lag an
    /// in-flight command — they are charged after the fact via
    /// <see cref="RecordExecutionAsync"/>.
    /// </remarks>
    /// <param name="threadId">Thread the next sandbox command will run under.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ThreadQuotaCheckResult> CheckAsync(Guid threadId, CancellationToken ct = default);

    /// <summary>
    /// Records the soft-cap cost (duration + output bytes) of a completed sandbox
    /// command. The window TTL is extended so an active thread keeps its counters
    /// alive while consuming. Note: the command-count counter is reserved in
    /// <see cref="CheckAsync"/> and is intentionally <b>not</b> touched here to
    /// avoid double-counting.
    /// </summary>
    /// <param name="threadId">Thread the command ran under.</param>
    /// <param name="durationMs">Wall-clock duration of the command (soft cap).</param>
    /// <param name="outputBytes">Combined stdout + stderr byte count (soft cap).</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordExecutionAsync(Guid threadId, long durationMs, long outputBytes, CancellationToken ct = default);

    /// <summary>
    /// Snapshots the thread's current usage. Returns a zero-valued usage
    /// object when nothing has been recorded yet.
    /// </summary>
    Task<ThreadQuotaUsage> GetUsageAsync(Guid threadId, CancellationToken ct = default);

    /// <summary>
    /// Drops all counters for the thread; the next <see cref="CheckAsync"/>
    /// will see a fresh quota. Used by tests and by admin-initiated thread
    /// resets.
    /// </summary>
    Task ResetAsync(Guid threadId, CancellationToken ct = default);
}
