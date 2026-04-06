namespace Tnzi.AI.Services;

/// <summary>
/// 空操作工作流服务 - 当未加载 Tnzi.AI.Workflow 模块时作为回退实现
/// </summary>
public class NoOpWorkflowService : IWorkflowService, INoOpService
{
    private const string Message =
        "Workflow features require Tnzi.AI.Workflow module. Add [DependsOn(typeof(AIWorkflowModule))] to enable workflow execution.";

    public Task<Result<WorkflowDefinitionDto>> CreateAsync(CreateWorkflowDefinitionDto input)
        => Task.FromResult(Result.Failure<WorkflowDefinitionDto>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<WorkflowDefinitionDto>> UpdateAsync(Guid id, UpdateWorkflowDefinitionDto input)
        => Task.FromResult(Result.Failure<WorkflowDefinitionDto>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result> DeleteAsync(Guid id)
        => Task.FromResult(Result.Failure(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<WorkflowDefinitionDto>> GetByIdAsync(Guid id)
        => Task.FromResult(Result.Failure<WorkflowDefinitionDto>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<IPagedList<WorkflowDefinitionDto>>> GetListAsync(WorkflowDefinitionQueryDto query)
        => Task.FromResult(Result.Failure<IPagedList<WorkflowDefinitionDto>>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<WorkflowExecutionResultDto>> RunAsync(Guid workflowId, string input, Guid? userId = null, CancellationToken ct = default)
        => Task.FromResult(Result.Failure<WorkflowExecutionResultDto>(Message, 501, ErrorCodes.WorkflowFailed));

    public async IAsyncEnumerable<WorkflowExecutionResultDto> RunStreamingAsync(
        Guid workflowId,
        string input,
        Guid? userId = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new WorkflowExecutionResultDto
        {
            Output = Message,
            Status = "Failed"
        };

        await Task.CompletedTask;
    }

    public Task<Result<WorkflowExecutionResultDto>> ResumeAsync(string executionId, CancellationToken ct = default)
        => Task.FromResult(Result.Failure<WorkflowExecutionResultDto>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<WorkflowExecutionResultDto>> ResumeWithInputAsync(string executionId, string stepId, Dictionary<string, object> input, CancellationToken ct = default)
        => Task.FromResult(Result.Failure<WorkflowExecutionResultDto>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result> ApproveStepAsync(string executionId, string stepId, string? feedback = null, CancellationToken ct = default)
        => Task.FromResult(Result.Failure(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result> RejectStepAsync(string executionId, string stepId, string reason, CancellationToken ct = default)
        => Task.FromResult(Result.Failure(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<WorkflowExecutionStatusDto>> GetExecutionStatusAsync(string executionId, CancellationToken ct = default)
        => Task.FromResult(Result.Failure<WorkflowExecutionStatusDto>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<WorkflowInterruptDto>> GetPendingInterruptAsync(string executionId, CancellationToken ct = default)
        => Task.FromResult(Result.Failure<WorkflowInterruptDto>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result> CancelAsync(string executionId, string? reason = null, CancellationToken ct = default)
        => Task.FromResult(Result.Failure(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<WorkflowDefinitionDto>> CloneAsync(Guid id, string? newName = null)
        => Task.FromResult(Result.Failure<WorkflowDefinitionDto>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<int>> BatchDeleteAsync(List<Guid> ids)
        => Task.FromResult(Result.Failure<int>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<int>> BatchSetEnabledAsync(List<Guid> ids, bool enabled)
        => Task.FromResult(Result.Failure<int>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<WorkflowStatsDto>> GetStatsAsync()
        => Task.FromResult(Result.Failure<WorkflowStatsDto>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<IPagedList<WorkflowExecutionSummaryDto>>> GetExecutionsAsync(WorkflowExecutionQueryDto query)
        => Task.FromResult(Result.Failure<IPagedList<WorkflowExecutionSummaryDto>>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<WorkflowExecutionDetailDto>> GetExecutionDetailAsync(string executionId)
        => Task.FromResult(Result.Failure<WorkflowExecutionDetailDto>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<WorkflowValidationResultDto>> ValidateAsync(Guid workflowId)
        => Task.FromResult(Result.Failure<WorkflowValidationResultDto>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<List<WorkflowDefinitionVersionDto>>> GetVersionHistoryAsync(Guid workflowId)
        => Task.FromResult(Result.Failure<List<WorkflowDefinitionVersionDto>>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<WorkflowDefinitionVersionDto>> GetVersionAsync(Guid workflowId, int versionNumber)
        => Task.FromResult(Result.Failure<WorkflowDefinitionVersionDto>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result> RestoreVersionAsync(Guid workflowId, int versionNumber, string? changeDescription = null)
        => Task.FromResult(Result.Failure(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<WorkflowExecutionStatsDto>> GetExecutionStatsAsync(Guid workflowId)
        => Task.FromResult(Result.Failure<WorkflowExecutionStatsDto>(Message, 501, ErrorCodes.WorkflowFailed));

    public Task<Result<List<WorkflowExecutionSignal>>> GetPendingSignalsAsync(string executionId, CancellationToken ct = default)
        => Task.FromResult(Result.Failure<List<WorkflowExecutionSignal>>(Message, 501, ErrorCodes.WorkflowFailed));
}
