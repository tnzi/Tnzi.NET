namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// Agent 任务管理控制器 - 查询 Agent 运行中的持久化任务列表
/// </summary>
[DefaultController]
[Route("admin/ai/tasks")]
[ApiAuthorize(PermissionName = "ai.agentRun.view")]
public class DefaultTaskAdminController : ApiAdminControllerBase
{
    private readonly IAgentTaskService _taskService;

    public DefaultTaskAdminController(IAgentTaskService taskService)
    {
        _taskService = Check.NotNull(taskService);
    }

    /// <summary>
    /// 按 RunId 获取任务列表
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<List<AgentTaskDto>>> GetByRunId([FromQuery] Guid runId)
    {
        var result = await _taskService.GetByRunIdAsync(runId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 按状态获取任务列表
    /// </summary>
    [HttpGet("by-status")]
    public virtual async Task<ApiResult<List<AgentTaskDto>>> GetByStatus([FromQuery] AgentTaskStatus status)
    {
        var result = await _taskService.GetByStatusAsync(status);
        return result.ToApiResult();
    }
}
