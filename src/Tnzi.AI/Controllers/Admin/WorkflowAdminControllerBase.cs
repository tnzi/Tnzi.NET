namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// 工作流控制器基类
/// 提供工作流 CRUD、运行等 API 端点，所有方法支持重写
/// </summary>
[Route("admin/workflows")]
[ApiExplorerSettings(GroupName = "ai-admin")]
public abstract class WorkflowAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IWorkflowService WorkflowService;

    /// <summary>
    /// 初始化工作流控制器基类
    /// </summary>
    protected WorkflowAdminControllerBase(IWorkflowService workflowService)
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
    public virtual async Task<ApiResult<IPagedList<WorkflowDefinitionDto>>> GetList([FromBody] PagedQueryDto query)
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
    /// 将 WorkflowExecutionResultDto 转换为 StreamEvent 以保持统一的流式格式
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
        var isCompleted = firstResult.Status == "Completed" || firstResult.Status.StartsWith("PartialFailure", StringComparison.Ordinal);
        await StreamingResponseWriter.WriteEventAsync(Response, new StreamEvent
        {
            Delta = firstResult.Output,
            FinishReason = isCompleted ? "stop" : null,
            IsDone = isCompleted
        }, format, cancellationToken);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                var result = enumerator.Current;
                isCompleted = result.Status == "Completed" || result.Status.StartsWith("PartialFailure", StringComparison.Ordinal);
                await StreamingResponseWriter.WriteEventAsync(Response, new StreamEvent
                {
                    Delta = result.Output,
                    FinishReason = isCompleted ? "stop" : null,
                    IsDone = isCompleted
                }, format, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            await StreamingResponseWriter.WriteErrorAsync(Response, ex.Message, ErrorCodes.StreamingFailed, format, CancellationToken.None);
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        await StreamingResponseWriter.WriteDoneAsync(Response, format, CancellationToken.None);
    }

}

