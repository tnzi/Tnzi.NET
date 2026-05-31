namespace Tnzi.AI.Services;

/// <summary>
/// AgentRun 信号分发器
/// </summary>
[ExperimentalApi(Reason = "Agent run signal dispatch is in preview")]
public class AgentRunSignalDispatcher : IAgentRunSignalDispatcher
{
    private readonly IRunStore _runStore;
    private readonly IAgentRunService _agentRunService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISubAgentRunCancellationRegistry? _cancellationRegistry;

    public AgentRunSignalDispatcher(
        IRunStore runStore,
        IAgentRunService agentRunService,
        IServiceProvider serviceProvider,
        ISubAgentRunCancellationRegistry? cancellationRegistry = null)
    {
        _runStore = Check.NotNull(runStore);
        _agentRunService = Check.NotNull(agentRunService);
        _serviceProvider = Check.NotNull(serviceProvider);
        _cancellationRegistry = cancellationRegistry;
    }

    public async Task<Result> DispatchInputAsync(Guid runId, SendAgentRunInput input, CancellationToken ct = default)
    {
        Check.NotNull(input);

        var run = await _runStore.GetWithNodesAsync(runId, ct);
        if (run == null)
            return Result.Failure("Run not found", 404, ErrorCodes.RunNotFound);

        if (string.IsNullOrWhiteSpace(run.WorkflowExecutionId) || !run.WorkflowDefinitionId.HasValue)
        {
            var resumeResult = await _agentRunService.ResumeAsync(runId, new ResumeRunInput
            {
                UserMessage = input.Message,
                WorkflowStepId = input.WorkflowStepId,
                WorkflowInput = input.WorkflowInput
            });

            return resumeResult.Succeeded
                ? Result.Success()
                : Result.Failure(resumeResult.Message ?? "Failed to dispatch run input", resumeResult.Code ?? 500, resumeResult.ErrorCode);
        }

        var workflowService = _serviceProvider.GetService<IWorkflowExecutionControlService>();
        var mailbox = _serviceProvider.GetService<IWorkflowExecutionMailbox>();
        if (workflowService == null)
            return Result.Failure("Workflow control service is not available", 501, ErrorCodes.WorkflowFailed);

        if (run.Status == AgentRunStatus.RequiresClarification)
        {
            if (input.WorkflowInput == null || input.WorkflowInput.Count == 0)
                return Result.Failure("Workflow structured input is required", 400, ErrorCodes.RunInvalidState);

            var stepId = input.WorkflowStepId;
            if (string.IsNullOrWhiteSpace(stepId))
            {
                var queryService = _serviceProvider.GetService<IWorkflowExecutionQueryService>();
                if (queryService == null)
                    return Result.Failure("Workflow query service is not available", 501, ErrorCodes.WorkflowFailed);

                var interrupt = await queryService.GetPendingInterruptAsync(run.WorkflowExecutionId!, ct);
                if (!interrupt.Succeeded || interrupt.Data == null || string.IsNullOrWhiteSpace(interrupt.Data.StepId))
                    return Result.Failure(interrupt.Message ?? "Failed to resolve workflow interrupt", interrupt.Code ?? 400, interrupt.ErrorCode);

                stepId = interrupt.Data.StepId;
            }

            var resume = await workflowService.ResumeWithInputAsync(run.WorkflowExecutionId!, stepId!, input.WorkflowInput, ct);
            return resume.Succeeded
                ? Result.Success()
                : Result.Failure(resume.Message ?? "Failed to resume workflow execution", resume.Code ?? 500, resume.ErrorCode);
        }

        if (mailbox == null)
            return Result.Failure("Workflow mailbox is not available", 501, ErrorCodes.WorkflowFailed);

        await mailbox.EnqueueSignalAsync(run.WorkflowExecutionId!, new WorkflowExecutionSignal
        {
            Type = WorkflowExecutionSignalTypes.ResumeInput,
            StepId = input.WorkflowStepId,
            NodeInput = new AgentRunNodeInput
            {
                NodeName = input.WorkflowStepId,
                InputKind = "human_input",
                Input = new WorkflowExecutionInput
                {
                    Message = input.Message,
                    Data = input.WorkflowInput
                }
            }
        }, ct);

        return Result.Success();
    }

    public async Task<Result> CancelAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _runStore.GetWithNodesAsync(runId, ct);
        if (run == null)
            return Result.Failure("Run not found", 404, ErrorCodes.RunNotFound);

        var cancelResult = await _agentRunService.CancelAsync(runId);
        if (!cancelResult.Succeeded)
        {
            return cancelResult;
        }

        // Trip the CTS so the in-process background Task actually stops
        _cancellationRegistry?.TryCancel(runId);

        if (string.IsNullOrWhiteSpace(run.WorkflowExecutionId) || !run.WorkflowDefinitionId.HasValue)
        {
            return Result.Success();
        }

        var workflowService = _serviceProvider.GetService<IWorkflowExecutionControlService>();
        if (workflowService == null)
            return Result.Success();

        var workflowResult = await workflowService.CancelAsync(run.WorkflowExecutionId!, $"Cancelled run {runId}", ct);
        return workflowResult.Succeeded
            ? Result.Success()
            : Result.Failure(workflowResult.Message ?? "Failed to cancel workflow execution", workflowResult.Code ?? 500, workflowResult.ErrorCode);
    }
}
