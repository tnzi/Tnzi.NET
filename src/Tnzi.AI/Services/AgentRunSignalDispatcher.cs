namespace Tnzi.AI.Services;

/// <summary>
/// AgentRun 信号分发器
/// </summary>
[ExperimentalApi(Reason = "Agent run signal dispatch is in preview")]
public class AgentRunSignalDispatcher : IAgentRunSignalDispatcher
{
    private readonly IRunStore _runStore;
    private readonly IAgentRunService _agentRunService;
    private readonly IWorkflowExecutionControlService? _workflowService;
    private readonly IWorkflowExecutionQueryService? _workflowQueryService;
    private readonly ISubAgentRunCancellationRegistry? _cancellationRegistry;
    private readonly IAgentExecutionContextAccessor? _executionContextAccessor;
    private readonly ICurrentUser? _currentUser;

    public AgentRunSignalDispatcher(
        IRunStore runStore,
        IAgentRunService agentRunService,
        IWorkflowExecutionControlService? workflowService = null,
        IWorkflowExecutionQueryService? workflowQueryService = null,
        ISubAgentRunCancellationRegistry? cancellationRegistry = null,
        IAgentExecutionContextAccessor? executionContextAccessor = null,
        ICurrentUser? currentUser = null)
    {
        _runStore = Check.NotNull(runStore);
        _agentRunService = Check.NotNull(agentRunService);
        _workflowService = workflowService;
        _workflowQueryService = workflowQueryService;
        _cancellationRegistry = cancellationRegistry;
        _executionContextAccessor = executionContextAccessor;
        _currentUser = currentUser;
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

        // Workflow 子接口由 DI 转发到 IWorkflowService（NoOpWorkflowService 在未加载 Workflow 模块时统一返回 501），
        // 因此不再做 null→501 防御分支。
        // ★ 「在等输入」以执行的真实状态为准，行上的 AgentRun.Status 只是它的投影：行只在引擎收尾
        // 与恢复时改写，修复前的引擎还把 HumanInput 中断写成 AwaitingApproval，按行判会把
        // GetState 报 canSendInput=true 的那条运行拒掉。与 AgentRuntimeControlService.GetStateAsync 同口径。
        if (run.Status == AgentRunStatus.RequiresClarification || await IsExecutionAwaitingInputAsync(run.WorkflowExecutionId!, ct))
        {
            if (input.WorkflowInput == null || input.WorkflowInput.Count == 0)
                return Result.Failure("Workflow structured input is required", 400, ErrorCodes.RunInvalidState);

            var stepId = input.WorkflowStepId;
            if (string.IsNullOrWhiteSpace(stepId))
            {
                var interrupt = await _workflowQueryService!.GetPendingInterruptAsync(run.WorkflowExecutionId!, ct);
                if (!interrupt.Succeeded || interrupt.Data == null || string.IsNullOrWhiteSpace(interrupt.Data.StepId))
                    return Result.Failure(interrupt.Message ?? "Failed to resolve workflow interrupt", interrupt.Code ?? 400, interrupt.ErrorCode);

                stepId = interrupt.Data.StepId;
            }

            var resume = await _workflowService!.ResumeWithInputAsync(run.WorkflowExecutionId!, stepId!, input.WorkflowInput, ct);
            return resume.Succeeded
                ? Result.Success()
                : Result.Failure(resume.Message ?? "Failed to resume workflow execution", resume.Code ?? 500, resume.ErrorCode);
        }

        // ★ 不再把输入包成 resume_input 信号丢进邮箱。引擎只应用 cancel 一种信号，此前这条路径
        // 返回 Success、PendingSignalCount +1，然后引擎在下一个层边界确认并丢弃它 —— 调用方被告知
        // "已接受"而没有任何节点看见过。输入到达工作流节点的唯一通道是 ResumeWithInputAsync，
        // 它要求执行正在等待输入；其余状态一律在这里拒绝，让 send_agent_input 说实话。
        return Result.Failure(
            $"Workflow run is not awaiting input (status: {run.Status}); input can only be delivered to a run in RequiresClarification state",
            409,
            ErrorCodes.RunInvalidState);
    }

    /// <summary>发起取消的人：正在执行的请求的用户（kill_agent 工具）> 环境用户（管理端）> 无从得知。</summary>
    private string BuildKillReason()
    {
        var userId = _executionContextAccessor?.CurrentRequest?.UserId ?? _currentUser?.Id;
        return userId.HasValue ? $"Cancelled by user {userId.Value}" : "Cancelled by request";
    }

    private async Task<bool> IsExecutionAwaitingInputAsync(string executionId, CancellationToken ct)
    {
        var status = await _workflowQueryService!.GetExecutionStatusAsync(executionId, ct);
        return status is { Succeeded: true, Data: not null }
            && string.Equals(status.Data.Status, nameof(WorkflowExecutionStatus.AwaitingInput), StringComparison.OrdinalIgnoreCase);
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

        // Trip the CTS so the in-process background Task actually stops. The reason travels with it:
        // AgentRuntime's cancellation branch writes it into AgentRun.Error, so a kill reads as a kill
        // (not as "The operation was canceled." shared with timeouts and real failures).
        _cancellationRegistry?.TryCancel(runId, BuildKillReason());

        if (string.IsNullOrWhiteSpace(run.WorkflowExecutionId) || !run.WorkflowDefinitionId.HasValue)
        {
            return Result.Success();
        }

        // 该 run 关联工作流执行：经 DI 转发的 IWorkflowExecutionControlService 取消（NoOp 在未加载 Workflow 模块时返回 501）。
        var workflowResult = await _workflowService!.CancelAsync(run.WorkflowExecutionId!, $"Cancelled run {runId}", ct);
        return workflowResult.Succeeded
            ? Result.Success()
            : Result.Failure(workflowResult.Message ?? "Failed to cancel workflow execution", workflowResult.Code ?? 500, workflowResult.ErrorCode);
    }
}
