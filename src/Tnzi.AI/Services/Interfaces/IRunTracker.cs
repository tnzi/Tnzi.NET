namespace Tnzi.AI.Services.Interfaces;

public interface IRunTracker
{
    Task<AgentRun> CreateRunAsync(AgentRunRequest request, AgentResolution resolution, CancellationToken ct);
    Task<AgentRun> GetOrCreateRunAsync(AgentRunRequest request, AgentResolution resolution, CancellationToken ct);
    Task<AgentRun?> GetWithNodesAsync(Guid runId, CancellationToken ct);
    Task UpdateAsync(AgentRun run, CancellationToken ct);
    Task UpdateNodeAsync(AgentRunNode node, CancellationToken ct);
    Task RecordTraceAsync(Guid runId, Guid? nodeId, string eventType, object? eventData, long durationMs, CancellationToken ct);
    Task UpdateRunOnCompletionAsync(AgentRun run, AgentRunResult result, long durationMs, CancellationToken ct);
    Task UpdateRunOnFailureAsync(AgentRun run, Exception? ex, long durationMs, CancellationToken ct);

    /// <summary>
    /// Finalize a streaming run that completed normally. Persists token usage, status,
    /// truncated response summary, and records a StreamCompleted trace.
    /// </summary>
    Task FinalizeStreamingCompletedAsync(AgentRun run, AgentRunResult streamResult,
        int totalInputTokens, int totalOutputTokens, long durationMs, CancellationToken ct);

    /// <summary>
    /// Finalize a streaming run that was cancelled by the caller. Records a StreamCancelled trace.
    /// </summary>
    Task FinalizeStreamingCancelledAsync(AgentRun run, string? lastFinishReason, long durationMs, CancellationToken ct);

    /// <summary>
    /// Finalize a streaming run that failed with an error. Records an Error trace.
    /// </summary>
    Task FinalizeStreamingFailedAsync(AgentRun run, string? lastFinishReason, long durationMs, CancellationToken ct);
}
