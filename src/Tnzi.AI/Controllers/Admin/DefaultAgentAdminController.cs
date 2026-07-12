using Tnzi.AI.Tools;

namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// Agent 管理控制器
/// 提供 Agent CRUD、运行等 API 端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/agents")]
[ApiAuthorize(PermissionName = "ai.agent.view")]
public class DefaultAgentAdminController : ApiAdminControllerBase
{
    protected readonly IAgentService AgentService;
    protected readonly IAgentValidationService ValidationService;
    protected readonly IToolRegistry ToolRegistry;

    /// <summary>
    /// 初始化 Agent 管理控制器
    /// </summary>
    public DefaultAgentAdminController(IAgentService agentService, IAgentValidationService validationService, IToolRegistry toolRegistry)
    {
        AgentService = Check.NotNull(agentService);
        ValidationService = Check.NotNull(validationService);
        ToolRegistry = Check.NotNull(toolRegistry);
    }

    /// <summary>
    /// 创建 Agent
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "ai.agent.create")]
    public virtual async Task<ApiResult<AgentDto>> Create([FromBody] CreateAgentDto input)
    {
        var result = await AgentService.CreateAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新 Agent
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "ai.agent.update")]
    public virtual async Task<ApiResult<AgentDto>> Update(Guid id, [FromBody] UpdateAgentDto input)
    {
        var result = await AgentService.UpdateAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除 Agent
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "ai.agent.delete")]
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
    [ApiAuthorize(PermissionName = "ai.agent.create")]
    public virtual async Task<ApiResult<AgentDto>> Clone(Guid id, [FromQuery] string? name = null)
    {
        var result = await AgentService.CloneAsync(id, name);
        return result.ToApiResult();
    }

    /// <summary>
    /// 运行 Agent
    /// </summary>
    [HttpPost("{id:guid}/run")]
    [ApiAuthorize(PermissionName = "ai.agent.execute")]
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
    [ApiAuthorize(PermissionName = "ai.agent.update")]
    public virtual async Task<ApiResult<AgentDto>> RollbackToVersion(Guid id, int version)
    {
        var result = await AgentService.RollbackToVersionAsync(id, version);
        return result.ToApiResult();
    }

    /// <summary>
    /// 配置 A/B 测试
    /// </summary>
    [HttpPost("{id:guid}/ab-test")]
    [ApiAuthorize(PermissionName = "ai.agent.update")]
    public virtual async Task<ApiResult<AgentDto>> ConfigureAbTest(Guid id, [FromBody] ConfigureAbTestDto input)
    {
        var result = await AgentService.ConfigureAbTestAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 停止 A/B 测试
    /// </summary>
    [HttpDelete("{id:guid}/ab-test")]
    [ApiAuthorize(PermissionName = "ai.agent.update")]
    public virtual async Task<ApiResult<AgentDto>> StopAbTest(Guid id)
    {
        var result = await AgentService.StopAbTestAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 流式运行 Agent（支持 SSE 和 NDJSON 格式）
    /// </summary>
    [HttpPost("{id:guid}/run/stream")]
    [ApiAuthorize(PermissionName = "ai.agent.execute")]
    public virtual async Task RunStreaming(Guid id, [FromBody] RunAgentRequestDto request, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUser?.Id ?? request.UserId;
        var format = StreamingResponseWriter.NegotiateFormat(Request);
        var stream = AgentService.RunStreamingAsync(id, request.Message, request.Content, request.ThreadId, userId, cancellationToken);
        await StreamingResponseWriter.WriteFullStreamAsync(Response, stream, format, cancellationToken);
    }

    /// <summary>
    /// 验证 Agent 配置有效性
    /// </summary>
    [HttpPost("{id:guid}/validate")]
    public virtual async Task<ApiResult<AgentValidationResultDto>> Validate(Guid id)
    {
        var result = await ValidationService.ValidateAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取所有 Agent 健康摘要
    /// </summary>
    [HttpGet("health")]
    public virtual async Task<ApiResult<AgentHealthSummaryDto>> GetHealth()
    {
        var result = await ValidationService.GetHealthSummaryAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取可分配的工具组目录（供 Agent 配置的工具组多选选择器，替代自由文本）
    /// </summary>
    [HttpGet("tool-groups")]
    public virtual ApiResult<List<ToolGroupDto>> GetToolGroups()
    {
        var groups = ToolRegistry.GetAllGroupNames()
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var tools = ToolRegistry.GetToolsByGroup(g);
                return new ToolGroupDto
                {
                    Name = g,
                    ToolCount = tools.Count,
                    ToolNames = tools.Select(t => t.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                };
            })
            .ToList();
        return ApiResult<List<ToolGroupDto>>.Ok(groups);
    }
}
