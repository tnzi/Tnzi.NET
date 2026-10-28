namespace Tnzi.AI.Workflow.Infrastructure;

/// <summary>
/// Ends the <see cref="AgentRun"/> projection of a workflow execution that left its wait without the engine
/// running again: the watchdog timed it out, an operator cancelled it, or an approval was rejected through the
/// workflow endpoints.
/// </summary>
/// <remarks>
/// The engine writes the run row only while it executes. An execution that ends while paused never reaches it, so
/// the run stayed <see cref="AgentRunStatus.AwaitingApproval"/> (or <see cref="AgentRunStatus.RequiresClarification"/>)
/// for good: the agent-runs list showed it waiting, and approving it there passed the run's status check and then
/// failed in the workflow service because the execution was no longer waiting. Only a run that is still waiting is
/// touched; a run some other path already ended keeps its outcome.
/// </remarks>
internal static class WaitingAgentRunCloser
{
    /// <summary>
    /// Move the waiting run linked to <paramref name="executionId"/> to <paramref name="status"/>. Returns whether
    /// a run was updated.
    /// </summary>
    public static async Task<bool> CloseAsync(
        IRepository<AgentRun, Guid> runRepository, string executionId, AgentRunStatus status, string? error, CancellationToken ct)
    {
        var run = await runRepository.FirstOrDefaultAsync(r => r.WorkflowExecutionId == executionId
            && (r.Status == AgentRunStatus.AwaitingApproval || r.Status == AgentRunStatus.RequiresClarification), ct);
        if (run == null)
        {
            return false;
        }

        run.Status = status;
        if (error != null)
        {
            run.Error = error;
        }

        run.LastHeartbeatAt = DateTime.UtcNow;
        await runRepository.UpdateAsync(run, ct);
        return true;
    }
}
