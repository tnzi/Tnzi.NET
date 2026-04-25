namespace Tnzi.AI.Infrastructure;

public class WorkflowDelegator : IWorkflowDelegator
{
    private readonly IWorkflowService _workflowService;
    private readonly IRunStore _runStore;
    private readonly IRunTracker _runTracker;
    private readonly ILogger<WorkflowDelegator> _logger;

    public WorkflowDelegator(IWorkflowService workflowService, IRunStore runStore,
        IRunTracker runTracker, ILogger<WorkflowDelegator> logger)
    {
        _workflowService = Check.NotNull(workflowService);
        _runStore = Check.NotNull(runStore);
        _runTracker = Check.NotNull(runTracker);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> ExecuteWorkflowAsync(AgentRunRequest request, CancellationToken ct)
    {
        var workflowId = request.WorkflowId!.Value;
        var input = request.UserMessage ?? string.Empty;
        var result = await _workflowService.RunAsync(workflowId, input, request.UserId, ct);

        if (!result.Succeeded || result.Data == null)
        {
            throw new BusinessException(
                result.Message ?? "Workflow execution failed",
                result.ErrorCode ?? ErrorCodes.WorkflowFailed,
                result.Code ?? 500);
        }

        return new AgentRunResult
        {
            Response = result.Data.Output,
            RunId = result.Data.RunId,
            FinishReason = MapFinishReason(result.Data.Status, streaming: false),
            Status = MapStatus(result.Data.Status)
        };
    }

    public async IAsyncEnumerable<AgentStreamChunk> ExecuteWorkflowStreamingAsync(
        AgentRunRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var workflowId = request.WorkflowId!.Value;
        var input = request.UserMessage ?? string.Empty;

        await foreach (var evt in _workflowService.RunStreamingAsync(workflowId, input, request.UserId, ct).WithCancellation(ct))
        {
            if (!string.IsNullOrWhiteSpace(evt.Output))
            {
                var isCompleted = IsTerminalStatus(evt.Status);

                yield return new AgentStreamChunk
                {
                    Text = evt.Output,
                    FinishReason = isCompleted
                        ? MapFinishReason(evt.Status, streaming: true)
                        : null
                };
            }
        }
    }

    public async Task<AgentRunResult> ResumeWorkflowRunAsync(AgentRun run, ResumeRunInput? input, CancellationToken ct)
    {
        var executionId = run.WorkflowExecutionId!;
        var requiresClarification = run.Status == AgentRunStatus.RequiresClarification;
        var awaitingNodes = run.Nodes
            .Where(n => n.Status == AgentRunNodeStatus.AwaitingApproval)
            .ToList();

        if (input?.ApprovalDecision != null)
        {
            var isApproved = input.ApprovalDecision.Equals("approve", StringComparison.OrdinalIgnoreCase);

            if (!isApproved)
            {
                foreach (var node in awaitingNodes)
                {
                    var rejectResult = await _workflowService.RejectStepAsync(
                        executionId, node.NodeName,
                        input.ApprovalComment ?? "Rejected by reviewer", ct);

                    if (!rejectResult.Succeeded)
                    {
                        throw new BusinessException(
                            rejectResult.Message ?? "Failed to reject workflow step",
                            rejectResult.ErrorCode ?? ErrorCodes.WorkflowFailed,
                            rejectResult.Code ?? 500);
                    }

                    node.Status = AgentRunNodeStatus.Rejected;
                    node.Error = input.ApprovalComment;
                    await _runStore.UpdateNodeAsync(node, ct);
                }

                run.Status = AgentRunStatus.Failed;
                run.Error = input.ApprovalComment ?? "Rejected by reviewer";
                await _runStore.UpdateAsync(run, ct);

                await _runTracker.RecordTraceAsync(run.Id, null, AgentTraceEventTypes.RunRejected,
                    new { workflowExecutionId = executionId, feedback = input.ApprovalComment }, 0, ct);

                return new AgentRunResult
                {
                    Response = run.Error,
                    RunId = run.Id,
                    ThreadId = run.ThreadId,
                    FinishReason = FinishReasons.Rejected,
                    Status = run.Status
                };
            }

            foreach (var node in awaitingNodes)
            {
                var approveResult = await _workflowService.ApproveStepAsync(
                    executionId, node.NodeName, input.ApprovalComment, ct);

                if (!approveResult.Succeeded)
                {
                    throw new BusinessException(
                        approveResult.Message ?? "Failed to approve workflow step",
                        approveResult.ErrorCode ?? ErrorCodes.WorkflowFailed,
                        approveResult.Code ?? 500);
                }

                node.Status = AgentRunNodeStatus.Approved;
                node.Output = input.ApprovalComment ?? node.Output;
                await _runStore.UpdateNodeAsync(node, ct);
            }
        }

        if (input?.RetryNodeId.HasValue == true)
        {
            var retryNode = run.Nodes.FirstOrDefault(n => n.Id == input.RetryNodeId.Value);
            if (retryNode != null)
            {
                retryNode.Status = AgentRunNodeStatus.Pending;
                retryNode.RetryCount++;
                retryNode.Error = null;
                await _runStore.UpdateNodeAsync(retryNode, ct);

                await _runTracker.RecordTraceAsync(run.Id, retryNode.Id, AgentTraceEventTypes.NodeRetryRequested,
                    new { workflowExecutionId = executionId, nodeName = retryNode.NodeName, retryCount = retryNode.RetryCount }, 0, ct);
            }
        }

        run.Status = AgentRunStatus.Running;
        await _runStore.UpdateAsync(run, ct);

        Result<WorkflowExecutionResultDto> resumeResult;
        if (requiresClarification)
        {
            if (input?.WorkflowInput == null || input.WorkflowInput.Count == 0)
            {
                throw new BusinessException(
                    "Workflow run requires structured input to resume",
                    ErrorCodes.RunInvalidState, 400);
            }

            var stepId = input.WorkflowStepId;
            if (string.IsNullOrWhiteSpace(stepId))
            {
                var interruptResult = await _workflowService.GetPendingInterruptAsync(executionId, ct);
                if (!interruptResult.Succeeded || interruptResult.Data == null || string.IsNullOrWhiteSpace(interruptResult.Data.StepId))
                {
                    throw new BusinessException(
                        interruptResult.Message ?? "Failed to resolve pending workflow interrupt",
                        interruptResult.ErrorCode ?? ErrorCodes.WorkflowExecutionInvalidState,
                        interruptResult.Code ?? 400);
                }

                stepId = interruptResult.Data.StepId;
            }

            resumeResult = await _workflowService.ResumeWithInputAsync(executionId, stepId, input.WorkflowInput, ct);
        }
        else
        {
            resumeResult = await _workflowService.ResumeAsync(executionId, ct);
        }

        if (!resumeResult.Succeeded || resumeResult.Data == null)
        {
            run.Status = AgentRunStatus.Failed;
            run.Error = resumeResult.Message ?? "Failed to resume workflow execution";
            await _runStore.UpdateAsync(run, CancellationToken.None);

            throw new BusinessException(
                run.Error,
                resumeResult.ErrorCode ?? ErrorCodes.WorkflowFailed,
                resumeResult.Code ?? 500);
        }

        run.Status = MapStatus(resumeResult.Data.Status);
        run.OutputSummary = StringTruncator.Truncate(resumeResult.Data.Output, 500);
        run.Error = run.Status == AgentRunStatus.Failed ? resumeResult.Data.Output : null;
        await _runStore.UpdateAsync(run, ct);

        await _runTracker.RecordTraceAsync(run.Id, null, AgentTraceEventTypes.RunResumed,
            new { workflowExecutionId = executionId, status = resumeResult.Data.Status }, 0, ct);

        return new AgentRunResult
        {
            Response = resumeResult.Data.Output,
            RunId = run.Id,
            ThreadId = run.ThreadId,
            FinishReason = MapFinishReason(resumeResult.Data.Status, streaming: false),
            Status = run.Status
        };
    }

    public bool IsTerminalStatus(string? status)
    {
        return string.Equals(status, nameof(WorkflowExecutionStatus.Completed), StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, nameof(WorkflowExecutionStatus.Failed), StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, nameof(WorkflowExecutionStatus.Cancelled), StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, nameof(WorkflowExecutionStatus.AwaitingInput), StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, nameof(WorkflowExecutionStatus.AwaitingApproval), StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(status) && status.StartsWith("PartialFailure", StringComparison.OrdinalIgnoreCase));
    }

    public AgentRunStatus MapStatus(string? status)
    {
        if (string.Equals(status, nameof(WorkflowExecutionStatus.AwaitingInput), StringComparison.OrdinalIgnoreCase))
            return AgentRunStatus.RequiresClarification;

        if (string.Equals(status, nameof(WorkflowExecutionStatus.AwaitingApproval), StringComparison.OrdinalIgnoreCase))
            return AgentRunStatus.AwaitingApproval;

        if (string.Equals(status, nameof(WorkflowExecutionStatus.Cancelled), StringComparison.OrdinalIgnoreCase))
            return AgentRunStatus.Cancelled;

        if (string.Equals(status, nameof(WorkflowExecutionStatus.Failed), StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(status) && status.StartsWith("PartialFailure", StringComparison.OrdinalIgnoreCase)))
            return AgentRunStatus.Failed;

        return AgentRunStatus.Completed;
    }

    public string MapFinishReason(string? status, bool streaming)
    {
        return MapStatus(status) switch
        {
            AgentRunStatus.RequiresClarification => FinishReasons.RequiresClarification,
            AgentRunStatus.AwaitingApproval => FinishReasons.AwaitingApproval,
            AgentRunStatus.Cancelled => FinishReasons.Cancelled,
            AgentRunStatus.Failed => FinishReasons.Failed,
            _ => streaming ? FinishReasons.Stop : FinishReasons.Completed
        };
    }
}
