namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 工作流执行查询服务
/// </summary>
[ExperimentalApi(Reason = "Workflow execution query is in preview")]
public interface IWorkflowExecutionQueryService
{
    Task<Result<WorkflowExecutionStatusDto>> GetExecutionStatusAsync(string executionId, CancellationToken ct = default);

    Task<Result<WorkflowExecutionDetailDto>> GetExecutionDetailAsync(string executionId);

    Task<Result<WorkflowInterruptDto>> GetPendingInterruptAsync(string executionId, CancellationToken ct = default);

    Task<Result<List<WorkflowExecutionSignal>>> GetPendingSignalsAsync(string executionId, CancellationToken ct = default);
}
