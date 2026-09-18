namespace Tnzi.AI.Services;

/// <summary>
/// Agent runtime 控制服务实现
/// </summary>
public class AgentRuntimeControlService : ApplicationService, IAgentRuntimeControlService
{
    private readonly ISubAgentExecutionService _subAgentExecutionService;
    private readonly IAgentRunSignalDispatcher _signalDispatcher;
    private readonly IRunStore _runStore;
    private readonly IWorkflowService _workflowService;
    private readonly ISubAgentRegistry _subAgentRegistry;
    private readonly IWorkflowExecutionQueryService? _workflowQueryService;
    private readonly IAgentExecutionContextAccessor? _executionContextAccessor;

    public AgentRuntimeControlService(
        ISubAgentExecutionService subAgentExecutionService,
        IAgentRunSignalDispatcher signalDispatcher,
        IRunStore runStore,
        IWorkflowService workflowService,
        ISubAgentRegistry subAgentRegistry,
        IServiceProvider serviceProvider,
        IWorkflowExecutionQueryService? workflowQueryService = null,
        IAgentExecutionContextAccessor? executionContextAccessor = null)
        : base(serviceProvider)
    {
        _subAgentExecutionService = Check.NotNull(subAgentExecutionService);
        _signalDispatcher = Check.NotNull(signalDispatcher);
        _runStore = Check.NotNull(runStore);
        _workflowService = Check.NotNull(workflowService);
        _subAgentRegistry = Check.NotNull(subAgentRegistry);
        _workflowQueryService = workflowQueryService;
        _executionContextAccessor = executionContextAccessor;
    }

    /// <summary>
    /// 调用方身份：当前运行请求的 UserId（后台子 Agent 的作用域里没有当前用户，只能靠它），无则环境用户。
    /// 与权限检查 / 权限规则评估 / 审批请求同源，不会各说各话。
    /// </summary>
    private Guid? ResolveCallerUserId() => _executionContextAccessor?.CurrentRequest?.UserId ?? CurrentUser?.Id;

    /// <summary>
    /// 按范围加载运行：<see cref="AgentRunAccessScope.Caller"/> 下归属人不是调用方的运行视为不存在（404，不泄露存在性）。
    /// 未知调用方（null）只看得见同样无主的运行 —— 不知道是谁不等于就是那个人。
    /// </summary>
    private async Task<AgentRun?> LoadVisibleRunAsync(Guid runId, AgentRunAccessScope scope, CancellationToken cancellationToken)
    {
        var run = await _runStore.GetWithNodesAsync(runId, cancellationToken);
        if (run == null || scope == AgentRunAccessScope.Tenant)
        {
            return run;
        }

        var caller = ResolveCallerUserId();
        if (run.CreatorId != caller)
        {
            Logger.LogDebug("Run {RunId} is owned by {OwnerId}; hidden from caller {CallerId}", runId, run.CreatorId, caller);
            return null;
        }

        return run;
    }

    public Task<Result<AgentRunControlStateDto>> SpawnAsync(SpawnAgentRunInput input, CancellationToken cancellationToken = default)
    {
        return _subAgentExecutionService.SpawnAsync(input, cancellationToken);
    }

    public async Task<Result<AgentRunControlStateDto>> GetStateAsync(Guid runId, AgentRunAccessScope scope = AgentRunAccessScope.Caller, CancellationToken cancellationToken = default)
    {
        var run = await LoadVisibleRunAsync(runId, scope, cancellationToken);
        if (run == null)
            return Fail<AgentRunControlStateDto>("Run not found", 404, ErrorCodes.RunNotFound);

        return Ok(await BuildStateAsync(run, cancellationToken));
    }

    public async Task<Result<AgentRunWaitResultDto>> WaitAsync(Guid runId, WaitAgentRunInput? input = null, AgentRunAccessScope scope = AgentRunAccessScope.Caller, CancellationToken cancellationToken = default)
    {
        input ??= new WaitAgentRunInput();

        var timeoutSeconds = Math.Clamp(input.TimeoutSeconds, 1, 900);
        var pollIntervalMs = Math.Clamp(input.PollIntervalMs, 100, 5000);
        var startedAt = DateTime.UtcNow;
        var pollCount = 0;

        AgentRunControlStateDto? state = null;
        while (true)
        {
            var stateResult = await GetStateAsync(runId, scope, cancellationToken);
            if (!stateResult.Succeeded || stateResult.Data == null)
                return Fail<AgentRunWaitResultDto>(
                    stateResult.Message ?? "Failed to get run state",
                    stateResult.Code ?? 500,
                    stateResult.ErrorCode ?? ErrorCodes.AgentRunFailed);

            state = stateResult.Data;
            pollCount++;

            if (state.IsTerminal)
            {
                break;
            }

            if (DateTime.UtcNow - startedAt >= TimeSpan.FromSeconds(timeoutSeconds))
            {
                break;
            }

            await Task.Delay(pollIntervalMs, cancellationToken);
        }

        var waited = DateTime.UtcNow - startedAt;
        return Ok(new AgentRunWaitResultDto
        {
            State = state!,
            TimedOut = !state!.IsTerminal,
            PollCount = pollCount,
            WaitedMs = (long)waited.TotalMilliseconds
        });
    }

    public async Task<Result<AgentRunControlStateDto>> SendInputAsync(Guid runId, SendAgentRunInput input, AgentRunAccessScope scope = AgentRunAccessScope.Caller, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var hasMessage = !string.IsNullOrWhiteSpace(input.Message);
        var hasWorkflowInput = input.WorkflowInput is { Count: > 0 };
        if (!hasMessage && !hasWorkflowInput)
            return Fail<AgentRunControlStateDto>("Message or workflow input is required", 400, ErrorCodes.RunInvalidState);

        if (await LoadVisibleRunAsync(runId, scope, cancellationToken) == null)
            return Fail<AgentRunControlStateDto>("Run not found", 404, ErrorCodes.RunNotFound);

        var resumeResult = await _signalDispatcher.DispatchInputAsync(runId, input, cancellationToken);

        if (!resumeResult.Succeeded)
        {
            return Fail<AgentRunControlStateDto>(
                resumeResult.Message ?? "Failed to send input to run",
                resumeResult.Code ?? 500,
                resumeResult.ErrorCode ?? ErrorCodes.AgentRunFailed);
        }

        return await GetStateAsync(runId, scope, cancellationToken);
    }

    public async Task<Result> KillAsync(Guid runId, AgentRunAccessScope scope = AgentRunAccessScope.Caller, CancellationToken cancellationToken = default)
    {
        if (await LoadVisibleRunAsync(runId, scope, cancellationToken) == null)
            return Fail("Run not found", 404, ErrorCodes.RunNotFound);

        return await _signalDispatcher.CancelAsync(runId, cancellationToken);
    }

    public async Task<Result<List<AgentRunListItemDto>>> ListRunsAsync(int maxResults = 20, AgentRunStatus? status = null, AgentRunAccessScope scope = AgentRunAccessScope.Caller, CancellationToken cancellationToken = default)
    {
        maxResults = Math.Clamp(maxResults, 1, 100);

        var runs = scope == AgentRunAccessScope.Tenant
            ? await _runStore.ListAsync(status, maxResults, cancellationToken)
            : await _runStore.ListByOwnerAsync(ResolveCallerUserId(), status, maxResults, cancellationToken);

        var items = runs.Select(r => new AgentRunListItemDto
        {
            RunId = r.Id,
            AgentId = r.AgentId,
            Status = r.Status,
            InputSummary = r.InputSummary,
            CreationTime = r.CreationTime
        }).ToList();

        return Ok(items);
    }

    public Task<Result<List<SubAgentTypeDto>>> ListSubAgentTypesAsync(CancellationToken cancellationToken = default)
    {
        var items = _subAgentRegistry.GetAllForTenant(SubAgentTenantKey.From(ServiceProvider.GetService<ICurrentTenant>()?.Id))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new SubAgentTypeDto
            {
                Name = x.Name,
                Description = x.Description,
                ToolGroups = x.ToolGroups.Any() ? x.ToolGroups.ToList() : null,
                ExcludedToolGroups = x.ExcludedToolGroups.Any() ? x.ExcludedToolGroups.ToList() : null,
                MaxTurns = x.MaxTurns,
                Instructions = x.Instructions,
                DefaultModel = x.DefaultModel,
                DefaultApprovalMode = x.DefaultApprovalMode,
                CapabilityTags = x.CapabilityTags?.Any() == true ? x.CapabilityTags.ToList() : null
            })
            .ToList();

        return Task.FromResult(Ok(items));
    }

    private async Task<AgentRunControlStateDto> BuildStateAsync(AgentRun run, CancellationToken cancellationToken)
    {
        var dto = new AgentRunControlStateDto
        {
            RunId = run.Id,
            AgentId = run.AgentId,
            ThreadId = run.ThreadId,
            WorkflowDefinitionId = run.WorkflowDefinitionId,
            WorkflowExecutionId = run.WorkflowExecutionId,
            ExecutionMode = run.ExecutionMode,
            Status = run.Status,
            InputSummary = run.InputSummary,
            OutputSummary = run.OutputSummary,
            Error = run.Error,
            AwaitingApprovalNodeNames = run.Nodes
                .Where(x => x.Status == AgentRunNodeStatus.AwaitingApproval)
                .OrderBy(x => x.OrderIndex)
                .Select(x => x.NodeName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            FailedNodeNames = run.Nodes
                .Where(x => x.Status == AgentRunNodeStatus.Failed)
                .OrderBy(x => x.OrderIndex)
                .Select(x => x.NodeName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            CanCancel = IsCancellable(run.Status),
            CanResume = IsResumable(run.Status),
            CanSendInput = run.Status == AgentRunStatus.RequiresClarification,
            RequiresUserAction = run.Status is AgentRunStatus.AwaitingApproval or AgentRunStatus.RequiresClarification,
            IsTerminal = IsObservableTerminal(run.Status),
            CreationTime = run.CreationTime,
            LastModificationTime = run.LastModificationTime
        };

        if (!string.IsNullOrWhiteSpace(run.WorkflowExecutionId))
        {
            var executionStatus = _workflowQueryService != null
                ? await _workflowQueryService.GetExecutionStatusAsync(run.WorkflowExecutionId, cancellationToken)
                : await _workflowService.GetExecutionStatusAsync(run.WorkflowExecutionId, cancellationToken);
            if (executionStatus.Succeeded && executionStatus.Data != null)
            {
                dto.WorkflowStatus = executionStatus.Data.Status;
                dto.AwaitingApprovalNodeNames = executionStatus.Data.StepsAwaitingApproval
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                dto.Status = MapWorkflowStatus(executionStatus.Data.Status, dto.Status, executionStatus.Data.CurrentWaitReason);
                dto.CanCancel = IsCancellable(dto.Status);
                dto.CanResume = IsResumable(dto.Status);
                dto.CanSendInput = dto.Status == AgentRunStatus.RequiresClarification;
                dto.RequiresUserAction = dto.Status is AgentRunStatus.AwaitingApproval or AgentRunStatus.RequiresClarification;
                dto.IsTerminal = IsObservableTerminal(dto.Status);
            }

            if (dto.RequiresUserAction)
            {
                var interrupt = _workflowQueryService != null
                    ? await _workflowQueryService.GetPendingInterruptAsync(run.WorkflowExecutionId, cancellationToken)
                    : await _workflowService.GetPendingInterruptAsync(run.WorkflowExecutionId, cancellationToken);
                if (interrupt.Succeeded && interrupt.Data != null && !string.IsNullOrWhiteSpace(interrupt.Data.StepId))
                {
                    dto.PendingInterrupt = interrupt.Data;
                }
            }
        }

        return dto;
    }

    private static bool IsCancellable(AgentRunStatus status)
    {
        return status is AgentRunStatus.Pending
            or AgentRunStatus.Running
            or AgentRunStatus.AwaitingApproval
            or AgentRunStatus.RequiresClarification;
    }

    private static bool IsResumable(AgentRunStatus status)
    {
        return status is AgentRunStatus.AwaitingApproval
            or AgentRunStatus.RequiresClarification
            or AgentRunStatus.Failed;
    }

    private static bool IsObservableTerminal(AgentRunStatus status)
    {
        return status is AgentRunStatus.Completed
            or AgentRunStatus.Failed
            or AgentRunStatus.Cancelled
            or AgentRunStatus.AwaitingApproval
            or AgentRunStatus.RequiresClarification;
    }

    private static AgentRunStatus MapWorkflowStatus(string? status, AgentRunStatus fallback, string? currentWaitReason)
    {
        if (fallback == AgentRunStatus.Cancelled
            && (string.IsNullOrWhiteSpace(status)
                || string.Equals(status, nameof(WorkflowExecutionStatus.Running), StringComparison.OrdinalIgnoreCase)
                || string.Equals(currentWaitReason, "cancel_requested", StringComparison.OrdinalIgnoreCase)))
        {
            return fallback;
        }

        if (string.Equals(status, "AwaitingInput", StringComparison.OrdinalIgnoreCase))
            return AgentRunStatus.RequiresClarification;

        if (string.Equals(status, "AwaitingApproval", StringComparison.OrdinalIgnoreCase))
            return AgentRunStatus.AwaitingApproval;

        if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            return AgentRunStatus.Cancelled;

        if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(status) && status.StartsWith("PartialFailure", StringComparison.OrdinalIgnoreCase)))
            return AgentRunStatus.Failed;

        if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
            return AgentRunStatus.Completed;

        return fallback;
    }
}
