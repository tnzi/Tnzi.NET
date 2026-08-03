namespace Tnzi.AI.Services;

/// <summary>
/// AgentRun 信号分发器
/// </summary>
[ExperimentalApi(Reason = "Agent run signal dispatch is in preview")]
public interface IAgentRunSignalDispatcher
{
    Task<Result> DispatchInputAsync(Guid runId, SendAgentRunInput input, CancellationToken ct = default);

    Task<Result> CancelAsync(Guid runId, CancellationToken ct = default);
}
