namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// Agent 运行管理控制器
/// 提供运行查询、节点查看、轨迹查看、取消/审批/重试等 API 端点
/// </summary>
[DefaultController]
[Route("admin/agent-runs")]
public class DefaultAgentRunAdminController : ApiAdminControllerBase
{
    protected readonly IAgentRunService RunService;
    protected readonly IAgentTraceService TraceService;

    /// <summary>
    /// 初始化 Agent 运行管理控制器
    /// </summary>
    public DefaultAgentRunAdminController(IAgentRunService runService, IAgentTraceService traceService)
    {
        RunService = Check.NotNull(runService);
        TraceService = Check.NotNull(traceService);
    }

    /// <summary>
    /// 获取运行统计
    /// </summary>
    [HttpGet("stats")]
    public virtual async Task<ApiResult<AgentRunStatsDto>> GetStats()
    {
        var result = await RunService.GetStatsAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 分页查询运行列表
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<AgentRunDto>>> GetList([FromBody] AgentRunQueryDto input)
    {
        var result = await RunService.GetListAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取运行详情
    /// </summary>
    [HttpGet("{runId:guid}")]
    public virtual async Task<ApiResult<AgentRunDto>> Get(Guid runId)
    {
        var result = await RunService.GetByIdAsync(runId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取运行的所有节点
    /// </summary>
    [HttpGet("{runId:guid}/nodes")]
    public virtual async Task<ApiResult<List<AgentRunNodeDto>>> GetNodes(Guid runId)
    {
        var result = await RunService.GetNodesAsync(runId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取指定节点详情
    /// </summary>
    [HttpGet("{runId:guid}/nodes/{nodeId:guid}")]
    public virtual async Task<ApiResult<AgentRunNodeDto>> GetNode(Guid runId, Guid nodeId)
    {
        var result = await RunService.GetNodeAsync(runId, nodeId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取指定节点的轨迹
    /// </summary>
    [HttpGet("{runId:guid}/nodes/{nodeId:guid}/traces")]
    public virtual async Task<ApiResult<List<AgentRunTraceDto>>> GetNodeTraces(Guid runId, Guid nodeId)
    {
        var result = await TraceService.GetByNodeAsync(runId, nodeId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取运行的完整轨迹
    /// </summary>
    [HttpGet("{runId:guid}/traces")]
    public virtual async Task<ApiResult<List<AgentRunTraceDto>>> GetRunTraces(Guid runId)
    {
        var result = await TraceService.GetByRunAsync(runId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 取消运行
    /// </summary>
    [HttpPost("{runId:guid}/cancel")]
    public virtual async Task<ApiResult> Cancel(Guid runId)
    {
        var result = await RunService.CancelAsync(runId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 审批通过（HITL）
    /// </summary>
    [HttpPost("{runId:guid}/approve")]
    public virtual async Task<ApiResult> Approve(Guid runId, [FromBody] ApproveRunDto? input)
    {
        var result = await RunService.ApproveAsync(runId, input?.Comment);
        return result.ToApiResult();
    }

    /// <summary>
    /// 审批拒绝（HITL）
    /// </summary>
    [HttpPost("{runId:guid}/reject")]
    public virtual async Task<ApiResult> Reject(Guid runId, [FromBody] RejectRunDto? input)
    {
        var result = await RunService.RejectAsync(runId, input?.Comment);
        return result.ToApiResult();
    }

    /// <summary>
    /// 重试失败节点
    /// </summary>
    [HttpPost("{runId:guid}/nodes/{nodeId:guid}/retry")]
    public virtual async Task<ApiResult> RetryNode(Guid runId, Guid nodeId)
    {
        var result = await RunService.RetryNodeAsync(runId, nodeId);
        return result.ToApiResult();
    }

}
