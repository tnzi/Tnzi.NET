namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// 工作流管理控制器
/// 提供工作流 CRUD、运行等 API 端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/workflows")]
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
    public virtual async Task<ApiResult<WorkflowDefinitionDto>> Create([FromBody] CreateWorkflowDefinitionDto input)
    {
        var result = await WorkflowService.CreateAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新工作流
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<WorkflowDefinitionDto>> Update(Guid id, [FromBody] UpdateWorkflowDefinitionDto input)
    {
        var result = await WorkflowService.UpdateAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除工作流
    /// </summary>
    [HttpDelete("{id:guid}")]
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
    [HttpPost("{id:guid}/run")]
    public virtual async Task<ApiResult<WorkflowExecutionResultDto>> Run(Guid id, [FromBody] RunWorkflowRequestDto request, CancellationToken cancellationToken = default)
    {
        // 管理员可代理但默认用自身 ID
        var userId = CurrentUser?.Id ?? request.UserId;
        var result = await WorkflowService.RunAsync(id, request.Input, userId, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 流式运行工作流（支持 SSE 和 NDJSON 格式）
    /// 输出工作流专用结构化流式事件，保留步骤状态和 StepResults 语义
    /// </summary>
    [HttpPost("{id:guid}/run/stream")]
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
            cancellationToken);
    }

    /// <summary>
    /// 克隆工作流（深拷贝定义，生成新 ID）
    /// </summary>
    [HttpPost("{id:guid}/clone")]
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
    /// 恢复暂停的工作流执行
    /// </summary>
    [HttpPost("executions/{executionId}/resume")]
    public virtual async Task<ApiResult<WorkflowExecutionResultDto>> ResumeExecution(string executionId, CancellationToken cancellationToken = default)
    {
        var result = await WorkflowService.ResumeAsync(executionId, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 使用外部输入恢复中断的工作流（通用 HITL 恢复）
    /// </summary>
    [HttpPost("executions/{executionId}/resume-with-input")]
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
    public virtual async Task<ApiResult> ApproveStep(string executionId, string stepId, [FromBody] WorkflowStepApprovalDto? input = null, CancellationToken cancellationToken = default)
    {
        var result = await WorkflowService.ApproveStepAsync(executionId, stepId, input?.Feedback, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 拒绝工作流步骤
    /// </summary>
    [HttpPost("executions/{executionId}/steps/{stepId}/reject")]
    public virtual async Task<ApiResult> RejectStep(string executionId, string stepId, [FromBody] WorkflowStepApprovalDto input, CancellationToken cancellationToken = default)
    {
        var result = await WorkflowService.RejectStepAsync(executionId, stepId, input.Feedback ?? "Rejected", cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除工作流
    /// </summary>
    [HttpPost("batch-delete")]
    public virtual async Task<ApiResult<int>> BatchDelete([FromBody] List<Guid> ids)
    {
        var result = await WorkflowService.BatchDeleteAsync(ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量启用工作流
    /// </summary>
    [HttpPost("batch-enable")]
    public virtual async Task<ApiResult<int>> BatchEnable([FromBody] List<Guid> ids)
    {
        var result = await WorkflowService.BatchSetEnabledAsync(ids, true);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量禁用工作流
    /// </summary>
    [HttpPost("batch-disable")]
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
        var isCompleted = result.Status == "Completed"
            || result.Status == "Failed"
            || result.Status == "AwaitingApproval"
            || result.Status == "AwaitingInput"
            || result.Status.StartsWith("PartialFailure", StringComparison.Ordinal);
        var stepId = result.StepResults is { Count: 1 } ? result.StepResults[0].StepId : null;

        return new WorkflowStreamEventDto
        {
            ExecutionId = result.ExecutionId,
            EventType = isCompleted ? WorkflowStreamEventTypes.Completed : WorkflowStreamEventTypes.Step,
            StepId = stepId,
            Status = result.Status,
            Output = result.Output,
            StepResults = result.StepResults,
            IsDone = isCompleted
        };
    }
}

