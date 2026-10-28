namespace Tnzi.AI.Workflow.Controllers.Admin;

/// <summary>
/// 工作流管理控制器
/// 提供工作流 CRUD、运行等 API 端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/workflows")]
[ApiAuthorize(PermissionName = "ai.workflow.view")]
public class DefaultWorkflowAdminController : ApiAdminControllerBase
{
    protected readonly IWorkflowService WorkflowService;

    /// <summary>
    /// 初始化工作流管理控制器
    /// </summary>
    public DefaultWorkflowAdminController(IWorkflowService workflowService)
    {
        WorkflowService = Check.NotNull(workflowService);
    }

    /// <summary>
    /// 创建工作流
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "ai.workflow.create")]
    public virtual async Task<ApiResult<WorkflowDefinitionDto>> Create([FromBody] CreateWorkflowDefinitionDto input)
    {
        var result = await WorkflowService.CreateAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新工作流
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "ai.workflow.update")]
    public virtual async Task<ApiResult<WorkflowDefinitionDto>> Update(Guid id, [FromBody] UpdateWorkflowDefinitionDto input)
    {
        var result = await WorkflowService.UpdateAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除工作流
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "ai.workflow.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await WorkflowService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据 ID 获取工作流
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<WorkflowDefinitionDto>> GetById(Guid id)
    {
        var result = await WorkflowService.GetByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取工作流列表
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<WorkflowDefinitionDto>>> GetList([FromBody] WorkflowDefinitionQueryDto query)
    {
        var result = await WorkflowService.GetListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 运行工作流
    /// </summary>
    /// <remarks>
    /// 运行与恢复四个端点不进请求级工作单元（<c>EnableGlobalUnitOfWork</c> 时也一样）：一次执行要跑数分钟，
    /// 执行行、检查点与终态在运行期间就必须对其他请求可见（状态查询、取消、心跳与看门狗），
    /// 而包在请求事务里时它们要到请求结束才提交，期间配额行的锁也一直被持有。
    /// </remarks>
    [HttpPost("{id:guid}/run")]
    [ApiAuthorize(PermissionName = "ai.workflow.execute")]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<ApiResult<WorkflowExecutionResultDto>> Run(Guid id, [FromBody] RunWorkflowRequestDto request, CancellationToken cancellationToken = default)
    {
        // 配额一律记在当前登录用户名下（防止请求体伪造他人 UserId）；
        // request.UserId 仅在无认证上下文时兜底。
        var userId = CurrentUser?.Id ?? request.UserId;
        var result = await WorkflowService.RunAsync(id, request.Input, userId, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 流式运行工作流（支持 SSE 和 NDJSON 格式）
    /// 输出工作流专用结构化流式事件，保留步骤状态和 StepResults 语义：
    /// 开始事件（<c>started</c>，带 executionId）→ 每个步骤完成时一个 <c>step</c> 事件 → <c>completed</c> 汇总事件。
    /// 一个节点跑得久时，事件之间每 <see cref="StreamHeartbeatInterval"/> 写一次心跳，反向代理不会把空闲连接切断。
    /// </summary>
    [HttpPost("{id:guid}/run/stream")]
    [ApiAuthorize(PermissionName = "ai.workflow.execute")]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task RunStreaming(Guid id, [FromBody] RunWorkflowRequestDto request, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUser?.Id ?? request.UserId;
        var format = StreamingResponseWriter.NegotiateFormat(Request);

        var stream = WorkflowService.RunStreamingAsync(id, request.Input, userId, cancellationToken);
        await StreamingResponseWriter.WriteFullStreamAsync(
            Response,
            stream,
            mapper: ToWorkflowStreamEvent,
            isDone: evt => evt.IsDone,
            errorFactory: ex => new WorkflowStreamEventDto
            {
                EventType = WorkflowStreamEventTypes.Error,
                Status = "Failed",
                ErrorMessage = ex is BusinessException ? ex.Message : "An internal error occurred",
                IsDone = true
            },
            format,
            cancellationToken,
            heartbeatInterval: StreamHeartbeatInterval);
    }

    /// <summary>
    /// 流式运行时两个事件之间最长的静默：一个节点可以合法地跑上几分钟，而常见反向代理的空闲超时是 60 到 100 秒。
    /// </summary>
    protected static readonly TimeSpan StreamHeartbeatInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 克隆工作流（深拷贝定义，生成新 ID）
    /// </summary>
    [HttpPost("{id:guid}/clone")]
    [ApiAuthorize(PermissionName = "ai.workflow.create")]
    public virtual async Task<ApiResult<WorkflowDefinitionDto>> Clone(Guid id, [FromBody] CloneWorkflowRequestDto? request = null)
    {
        var result = await WorkflowService.CloneAsync(id, request?.NewName);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取工作流执行状态
    /// </summary>
    [HttpGet("executions/{executionId}/status")]
    public virtual async Task<ApiResult<WorkflowExecutionStatusDto>> GetExecutionStatus(string executionId, CancellationToken cancellationToken = default)
    {
        var result = await WorkflowService.GetExecutionStatusAsync(executionId, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取工作流执行邮箱中的待处理信号
    /// </summary>
    [HttpGet("executions/{executionId}/signals")]
    public virtual async Task<ApiResult<List<WorkflowExecutionSignal>>> GetPendingSignals(string executionId, CancellationToken cancellationToken = default)
    {
        var result = await WorkflowService.GetPendingSignalsAsync(executionId, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 恢复暂停的工作流执行
    /// </summary>
    [HttpPost("executions/{executionId}/resume")]
    [ApiAuthorize(PermissionName = "ai.workflow.execute")]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<ApiResult<WorkflowExecutionResultDto>> ResumeExecution(string executionId, CancellationToken cancellationToken = default)
    {
        var result = await WorkflowService.ResumeAsync(executionId, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 取消工作流执行
    /// </summary>
    [HttpPost("executions/{executionId}/cancel")]
    [ApiAuthorize(PermissionName = "ai.workflow.execute")]
    public virtual async Task<ApiResult> CancelExecution(string executionId, [FromBody] WorkflowStepApprovalDto? input = null, CancellationToken cancellationToken = default)
    {
        var result = await WorkflowService.CancelAsync(executionId, input?.Feedback, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 使用外部输入恢复中断的工作流（通用 HITL 恢复）
    /// </summary>
    [HttpPost("executions/{executionId}/resume-with-input")]
    [ApiAuthorize(PermissionName = "ai.workflow.execute")]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task<ApiResult<WorkflowExecutionResultDto>> ResumeWithInput(string executionId, [FromBody] ResumeWorkflowInputDto input, CancellationToken cancellationToken = default)
    {
        var result = await WorkflowService.ResumeWithInputAsync(executionId, input.StepId, input.Input, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取工作流执行的待处理中断
    /// </summary>
    [HttpGet("executions/{executionId}/interrupt")]
    public virtual async Task<ApiResult<WorkflowInterruptDto>> GetPendingInterrupt(string executionId, CancellationToken cancellationToken = default)
    {
        var result = await WorkflowService.GetPendingInterruptAsync(executionId, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 审批工作流步骤
    /// </summary>
    [HttpPost("executions/{executionId}/steps/{stepId}/approve")]
    [ApiAuthorize(PermissionName = "ai.workflow.execute")]
    public virtual async Task<ApiResult> ApproveStep(string executionId, string stepId, [FromBody] WorkflowStepApprovalDto? input = null, CancellationToken cancellationToken = default)
    {
        var result = await WorkflowService.ApproveStepAsync(executionId, stepId, input?.Feedback, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 拒绝工作流步骤
    /// </summary>
    [HttpPost("executions/{executionId}/steps/{stepId}/reject")]
    [ApiAuthorize(PermissionName = "ai.workflow.execute")]
    public virtual async Task<ApiResult> RejectStep(string executionId, string stepId, [FromBody] WorkflowStepApprovalDto input, CancellationToken cancellationToken = default)
    {
        var result = await WorkflowService.RejectStepAsync(executionId, stepId, input.Feedback ?? "Rejected", cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除工作流
    /// </summary>
    [HttpPost("batch-delete")]
    [ApiAuthorize(PermissionName = "ai.workflow.delete")]
    public virtual async Task<ApiResult<int>> BatchDelete([FromBody] List<Guid> ids)
    {
        var result = await WorkflowService.BatchDeleteAsync(ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量启用工作流
    /// </summary>
    [HttpPost("batch-enable")]
    [ApiAuthorize(PermissionName = "ai.workflow.update")]
    public virtual async Task<ApiResult<int>> BatchEnable([FromBody] List<Guid> ids)
    {
        var result = await WorkflowService.BatchSetEnabledAsync(ids, true);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量禁用工作流
    /// </summary>
    [HttpPost("batch-disable")]
    [ApiAuthorize(PermissionName = "ai.workflow.update")]
    public virtual async Task<ApiResult<int>> BatchDisable([FromBody] List<Guid> ids)
    {
        var result = await WorkflowService.BatchSetEnabledAsync(ids, false);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取工作流统计
    /// </summary>
    [HttpGet("stats")]
    public virtual async Task<ApiResult<WorkflowStatsDto>> GetStats()
    {
        var result = await WorkflowService.GetStatsAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 查询工作流执行历史
    /// </summary>
    [HttpGet("executions")]
    public virtual async Task<ApiResult<IPagedList<WorkflowExecutionSummaryDto>>> GetExecutions([FromQuery] WorkflowExecutionQueryDto query)
    {
        var result = await WorkflowService.GetExecutionsAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取执行详情
    /// </summary>
    [HttpGet("executions/{executionId}/detail")]
    public virtual async Task<ApiResult<WorkflowExecutionDetailDto>> GetExecutionDetail(string executionId)
    {
        var result = await WorkflowService.GetExecutionDetailAsync(executionId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 验证工作流定义（预运行检查）
    /// </summary>
    [HttpPost("{id:guid}/validate")]
    public virtual async Task<ApiResult<WorkflowValidationResultDto>> Validate(Guid id)
    {
        var result = await WorkflowService.ValidateAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取工作流版本历史
    /// </summary>
    [HttpGet("{id:guid}/versions")]
    public virtual async Task<ApiResult<List<WorkflowDefinitionVersionDto>>> GetVersionHistory(Guid id)
    {
        var result = await WorkflowService.GetVersionHistoryAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取指定版本详情
    /// </summary>
    [HttpGet("{id:guid}/versions/{versionNumber:int}")]
    public virtual async Task<ApiResult<WorkflowDefinitionVersionDto>> GetVersion(Guid id, int versionNumber)
    {
        var result = await WorkflowService.GetVersionAsync(id, versionNumber);
        return result.ToApiResult();
    }

    /// <summary>
    /// 恢复到指定版本
    /// </summary>
    [HttpPost("{id:guid}/versions/{versionNumber:int}/restore")]
    [ApiAuthorize(PermissionName = "ai.workflow.update")]
    public virtual async Task<ApiResult> RestoreVersion(Guid id, int versionNumber, [FromBody] RestoreWorkflowVersionRequestDto? request = null)
    {
        var result = await WorkflowService.RestoreVersionAsync(id, versionNumber, request?.ChangeDescription);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取工作流执行统计（耗时分析）
    /// </summary>
    [HttpGet("{id:guid}/execution-stats")]
    public virtual async Task<ApiResult<WorkflowExecutionStatsDto>> GetExecutionStats(Guid id)
    {
        var result = await WorkflowService.GetExecutionStatsAsync(id);
        return result.ToApiResult();
    }

    private static WorkflowStreamEventDto ToWorkflowStreamEvent(WorkflowExecutionResultDto result)
    {
        var isCompleted = result.Status == nameof(WorkflowExecutionStatus.Completed)
            || result.Status == nameof(WorkflowExecutionStatus.Failed)
            || result.Status == nameof(WorkflowExecutionStatus.Cancelled)
            || result.Status == nameof(WorkflowExecutionStatus.AwaitingApproval)
            || result.Status == nameof(WorkflowExecutionStatus.AwaitingInput)
            || result.Status.StartsWith("PartialFailure", StringComparison.Ordinal);
        var stepId = result.StepResults is { Count: 1 } ? result.StepResults[0].StepId : null;
        var isStarted = result.Status == nameof(WorkflowExecutionStatus.Running) && result.StepResults == null;

        return new WorkflowStreamEventDto
        {
            ExecutionId = result.ExecutionId,
            EventType = isCompleted
                ? WorkflowStreamEventTypes.Completed
                : isStarted ? WorkflowStreamEventTypes.Started : WorkflowStreamEventTypes.Step,
            StepId = stepId,
            Status = result.Status,
            Output = result.Output,
            StepResults = result.StepResults,
            IsDone = isCompleted
        };
    }
}

