namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// Agent 运行管理控制器
/// 提供运行查询、节点查看、轨迹查看、取消/审批/重试等 API 端点
/// </summary>
[DefaultController]
[Route("admin/agent-runs")]
[ApiAuthorize(PermissionName = "ai.agentRun.view")]
public class DefaultAgentRunAdminController : ApiAdminControllerBase
{
    protected readonly IAgentRunService RunService;
    protected readonly IAgentTraceService TraceService;
    protected readonly IAgentRuntimeControlService RuntimeControlService;

    /// <summary>
    /// 初始化 Agent 运行管理控制器
    /// </summary>
    public DefaultAgentRunAdminController(
        IAgentRunService runService,
        IAgentTraceService traceService,
        IAgentRuntimeControlService runtimeControlService)
    {
        RunService = Check.NotNull(runService);
        TraceService = Check.NotNull(traceService);
        RuntimeControlService = Check.NotNull(runtimeControlService);
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
    /// 启动后台 AgentRun
    /// </summary>
    [HttpPost("spawn")]
    [ApiAuthorize(PermissionName = "ai.agentRun.execute")]
    public virtual async Task<ApiResult<AgentRunControlStateDto>> Spawn([FromBody] SpawnAgentRunInput input)
    {
        var result = await RuntimeControlService.SpawnAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取运行时控制状态
    /// </summary>
    [HttpGet("{runId:guid}/state")]
    public virtual async Task<ApiResult<AgentRunControlStateDto>> GetState(Guid runId)
    {
        var result = await RuntimeControlService.GetStateAsync(runId);
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
    [ApiAuthorize(PermissionName = "ai.agentRun.execute")]
    public virtual async Task<ApiResult> Cancel(Guid runId)
    {
        var result = await RunService.CancelAsync(runId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 等待运行进入可观察稳定状态
    /// </summary>
    [HttpPost("{runId:guid}/wait")]
    public virtual async Task<ApiResult<AgentRunWaitResultDto>> Wait(Guid runId, [FromBody] WaitAgentRunInput? input)
    {
        var result = await RuntimeControlService.WaitAsync(runId, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 恢复运行（失败重试 / 澄清输入）
    /// </summary>
    [HttpPost("{runId:guid}/resume")]
    [ApiAuthorize(PermissionName = "ai.agentRun.execute")]
    public virtual async Task<ApiResult<AgentResponseDto>> Resume(Guid runId, [FromBody] ResumeRunInput? input)
    {
        var result = await RunService.ResumeAsync(runId, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 发送额外输入并恢复运行
    /// </summary>
    [HttpPost("{runId:guid}/send-input")]
    [ApiAuthorize(PermissionName = "ai.agentRun.execute")]
    public virtual async Task<ApiResult<AgentRunControlStateDto>> SendInput(Guid runId, [FromBody] SendAgentRunInput input)
    {
        var result = await RuntimeControlService.SendInputAsync(runId, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 审批通过（HITL）
    /// </summary>
    [HttpPost("{runId:guid}/approve")]
    [ApiAuthorize(PermissionName = "ai.agentRun.execute")]
    public virtual async Task<ApiResult> Approve(Guid runId, [FromBody] ApproveRunDto? input)
    {
        var result = await RunService.ApproveAsync(runId, input?.Comment);
        return result.ToApiResult();
    }

    /// <summary>
    /// 审批拒绝（HITL）
    /// </summary>
    [HttpPost("{runId:guid}/reject")]
    [ApiAuthorize(PermissionName = "ai.agentRun.execute")]
    public virtual async Task<ApiResult> Reject(Guid runId, [FromBody] RejectRunDto? input)
    {
        var result = await RunService.RejectAsync(runId, input?.Comment);
        return result.ToApiResult();
    }

    /// <summary>
    /// 重试失败节点
    /// </summary>
    [HttpPost("{runId:guid}/nodes/{nodeId:guid}/retry")]
    [ApiAuthorize(PermissionName = "ai.agentRun.execute")]
    public virtual async Task<ApiResult> RetryNode(Guid runId, Guid nodeId)
    {
        var result = await RunService.RetryNodeAsync(runId, nodeId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取可用的子 Agent 类型模板
    /// </summary>
    [HttpGet("sub-agent-types")]
    public virtual async Task<ApiResult<List<SubAgentTypeDto>>> GetSubAgentTypes()
    {
        var result = await RuntimeControlService.ListSubAgentTypesAsync();
        return result.ToApiResult();
    }

}
