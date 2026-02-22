namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// Agent 管理控制器基类
/// 提供 Agent CRUD、运行等 API 端点，所有方法支持重写
/// </summary>
[Route("admin/agents")]
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
    /// 运行 Agent
    /// </summary>
    [HttpPost("{id:guid}/run")]
    public virtual async Task<ApiResult<AgentResponseDto>> Run(Guid id, [FromBody] RunAgentRequestDto request, CancellationToken cancellationToken = default)
    {
        var result = await AgentService.RunAsync(id, request.Message, request.Content, request.ThreadId, request.UserId, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 流式运行 Agent（支持 SSE 和 NDJSON 格式）
    /// </summary>
    [HttpPost("{id:guid}/run/stream")]
    public virtual async Task RunStreaming(Guid id, [FromBody] RunAgentRequestDto request, CancellationToken cancellationToken = default)
    {
        var format = StreamingResponseWriter.NegotiateFormat(Request);
        StreamingResponseWriter.ConfigureResponse(Response, format);

        await foreach (var evt in AgentService.RunStreamingAsync(id, request.Message, request.Content, request.ThreadId, request.UserId, cancellationToken))
        {
            await StreamingResponseWriter.WriteEventAsync(Response, evt, format, cancellationToken);
        }

        await StreamingResponseWriter.WriteDoneAsync(Response, format, cancellationToken);
    }

}
