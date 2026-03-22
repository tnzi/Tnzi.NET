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
        // 管理员可代理但默认用自身 ID
        var userId = CurrentUser?.Id ?? request.UserId;
        var format = StreamingResponseWriter.NegotiateFormat(Request);

        var stream = WorkflowService.RunStreamingAsync(id, request.Input, userId, cancellationToken);
        var enumerator = stream.GetAsyncEnumerator(cancellationToken);

        try
        {
            if (!await enumerator.MoveNextAsync())
            {
                StreamingResponseWriter.ConfigureResponse(Response, format);
                await StreamingResponseWriter.WriteDoneAsync(Response, format, cancellationToken);
                return;
            }
        }
        catch (BusinessException)
        {
            throw;
        }

        StreamingResponseWriter.ConfigureResponse(Response, format);

        var firstResult = enumerator.Current;
        await StreamingResponseWriter.WriteEventAsync(Response, ToWorkflowStreamEvent(firstResult), format, cancellationToken);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                var result = enumerator.Current;
                await StreamingResponseWriter.WriteEventAsync(Response, ToWorkflowStreamEvent(result), format, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            await StreamingResponseWriter.WriteEventAsync(Response, new WorkflowStreamEventDto
            {
                EventType = WorkflowStreamEventTypes.Error,
                Status = "Failed",
                ErrorMessage = ex.Message,
                IsDone = true
            }, format, CancellationToken.None);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        await StreamingResponseWriter.WriteDoneAsync(Response, format, CancellationToken.None);
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

    private static WorkflowStreamEventDto ToWorkflowStreamEvent(WorkflowExecutionResultDto result)
    {
        var isCompleted = result.Status == "Completed"
            || result.Status == "Failed"
            || result.Status == "AwaitingApproval"
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

