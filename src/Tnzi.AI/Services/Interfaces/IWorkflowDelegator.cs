namespace Tnzi.AI.Services;

public interface IWorkflowDelegator
{
    Task<AgentRunResult> ExecuteWorkflowAsync(AgentRunRequest request, CancellationToken ct);
    IAsyncEnumerable<AgentStreamChunk> ExecuteWorkflowStreamingAsync(AgentRunRequest request, CancellationToken ct);
    Task<AgentRunResult> ResumeWorkflowRunAsync(AgentRun run, ResumeRunInput? input, CancellationToken ct);

    bool IsTerminalStatus(string? status);
    AgentRunStatus MapStatus(string? status);
    string MapFinishReason(string? status, bool streaming);
}
