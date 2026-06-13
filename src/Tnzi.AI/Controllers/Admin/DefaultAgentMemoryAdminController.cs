namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// Agent 记忆管理控制器 — 管理端为某个 Agent 预置/管理其长期记忆（agent-bound）。
/// </summary>
[DefaultController]
[Route("admin/agents/{agentId:guid}/memory")]
public class DefaultAgentMemoryAdminController : ApiAdminControllerBase
{
    protected readonly IAgentMemoryService MemoryService;

    /// <summary>
    /// 初始化 Agent 记忆管理控制器
    /// </summary>
    public DefaultAgentMemoryAdminController(IAgentMemoryService memoryService)
    {
        MemoryService = Check.NotNull(memoryService);
    }

    /// <summary>
    /// 分页查询某 Agent 的记忆条目
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<AgentMemoryDto>>> GetList(Guid agentId, [FromBody] AgentMemoryListQueryDto query)
    {
        var result = await MemoryService.GetListAsync(agentId, query, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 为某 Agent 创建一条记忆
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<AgentMemoryDto>> Create(Guid agentId, [FromBody] CreateAgentMemoryDto dto)
    {
        var result = await MemoryService.CreateAsync(agentId, dto, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新某 Agent 的一条记忆
    /// </summary>
    [HttpPut("{memoryId:guid}")]
    public virtual async Task<ApiResult<AgentMemoryDto>> Update(Guid agentId, Guid memoryId, [FromBody] UpdateAgentMemoryDto dto)
    {
        var result = await MemoryService.UpdateAsync(agentId, memoryId, dto, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除某 Agent 的一条记忆
    /// </summary>
    [HttpDelete("{memoryId:guid}")]
    public virtual async Task<ApiResult> Delete(Guid agentId, Guid memoryId)
    {
        var result = await MemoryService.DeleteAsync(agentId, memoryId, HttpContext.RequestAborted);
        return result.ToApiResult();
    }
}
