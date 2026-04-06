namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 工作流执行控制服务
/// </summary>
[ExperimentalApi(Reason = "Workflow execution control is in preview")]
public interface IWorkflowExecutionControlService
{
    Task<Result<WorkflowExecutionResultDto>> ResumeAsync(string executionId, CancellationToken ct = default);

    Task<Result<WorkflowExecutionResultDto>> ResumeWithInputAsync(string executionId, string stepId, Dictionary<string, object> input, CancellationToken ct = default);

    Task<Result> ApproveStepAsync(string executionId, string stepId, string? feedback = null, CancellationToken ct = default);

    Task<Result> RejectStepAsync(string executionId, string stepId, string reason, CancellationToken ct = default);

    Task<Result> CancelAsync(string executionId, string? reason = null, CancellationToken ct = default);
}
