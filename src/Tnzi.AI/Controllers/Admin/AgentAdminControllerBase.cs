namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// Agent 管理控制器基类
/// 提供 Agent CRUD、运行等 API 端点，所有方法支持重写
/// </summary>
[Route("admin/agents")]
[ApiExplorerSettings(GroupName = "ai-admin")]
public abstract class AgentAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IAgentService AgentService;

    /// <summary>
    /// 初始化 Agent 管理控制器基类
    /// </summary>
    protected AgentAdminControllerBase(IAgentService agentService)
    {
        AgentService = Check.NotNull(agentService);
    }

    /// <summary>
    /// 创建 Agent
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<AgentDto>> Create([FromBody] CreateAgentDto input)
    {
        var result = await AgentService.CreateAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新 Agent
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<AgentDto>> Update(Guid id, [FromBody] UpdateAgentDto input)
    {
        var result = await AgentService.UpdateAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除 Agent
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await AgentService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据 ID 获取 Agent
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<AgentDto>> GetById(Guid id)
    {
        var result = await AgentService.GetByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取 Agent 列表
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<AgentDto>>> GetList([FromBody] AgentListQueryDto query)
    {
        var result = await AgentService.GetListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 克隆 Agent
    /// </summary>
    [HttpPost("{id:guid}/clone")]
    public virtual async Task<ApiResult<AgentDto>> Clone(Guid id, [FromQuery] string? name = null)
    {
        var result = await AgentService.CloneAsync(id, name);
        return result.ToApiResult();
    }

    /// <summary>
    /// 运行 Agent
    /// </summary>
    [HttpPost("{id:guid}/run")]
    public virtual async Task<ApiResult<AgentResponseDto>> Run(Guid id, [FromBody] RunAgentRequestDto request, CancellationToken cancellationToken = default)
    {
        // 管理员可代理但默认用自身 ID
        var userId = CurrentUser?.Id ?? request.UserId;
        var result = await AgentService.RunAsync(id, request.Message, request.Content, request.ThreadId, userId, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取 Agent 版本列表
    /// </summary>
    [HttpPost("{id:guid}/versions/query")]
    public virtual async Task<ApiResult<IPagedList<AgentVersionDto>>> GetVersions(Guid id, [FromBody] AgentVersionQueryDto query)
    {
        var result = await AgentService.GetVersionsAsync(id, query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取指定版本详情
    /// </summary>
    [HttpGet("{id:guid}/versions/{version:int}")]
    public virtual async Task<ApiResult<AgentVersionDto>> GetVersion(Guid id, int version)
    {
        var result = await AgentService.GetVersionAsync(id, version);
        return result.ToApiResult();
    }

    /// <summary>
    /// 回滚到指定版本
    /// </summary>
    [HttpPost("{id:guid}/versions/{version:int}/rollback")]
    public virtual async Task<ApiResult<AgentDto>> RollbackToVersion(Guid id, int version)
    {
        var result = await AgentService.RollbackToVersionAsync(id, version);
        return result.ToApiResult();
    }

    /// <summary>
    /// 流式运行 Agent（支持 SSE 和 NDJSON 格式）
    /// </summary>
    [HttpPost("{id:guid}/run/stream")]
    public virtual async Task RunStreaming(Guid id, [FromBody] RunAgentRequestDto request, CancellationToken cancellationToken = default)
    {
        // 管理员可代理但默认用自身 ID
        var userId = CurrentUser?.Id ?? request.UserId;
        var format = StreamingResponseWriter.NegotiateFormat(Request);

        var stream = AgentService.RunStreamingAsync(id, request.Message, request.Content, request.ThreadId, userId, cancellationToken);
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

        var firstEvent = enumerator.Current;
        await StreamingResponseWriter.WriteEventAsync(Response, firstEvent, format, cancellationToken);
        if (firstEvent.IsToolCall)
        {
            await StreamingResponseWriter.WriteHeartbeatAsync(Response, format, cancellationToken);
        }

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                var evt = enumerator.Current;
                await StreamingResponseWriter.WriteEventAsync(Response, evt, format, cancellationToken);
                if (evt.IsToolCall)
                {
                    await StreamingResponseWriter.WriteHeartbeatAsync(Response, format, cancellationToken);
                }
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
