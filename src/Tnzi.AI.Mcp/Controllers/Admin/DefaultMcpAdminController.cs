namespace Tnzi.AI.Mcp.Controllers.Admin;

/// <summary>
/// MCP Server 管理控制器 - 查看服务器状态、工具列表、动态管理暴露的 Agent
/// </summary>
/// <remarks>
/// 仅管理 Tnzi 自身作为 MCP Server 的配置（status/tools/agents）。
/// 外部 MCP Server 注册条目的 CRUD 由 <c>DefaultMcpClientAdminController</c>（Tnzi.AI 核心）负责。
/// </remarks>
[DefaultController]
[Route("admin/mcp")]
[ApiExplorerSettings(GroupName = "admin")]
[ApiAuthorize(PermissionName = "ai.mcpServer.view")]
public class DefaultMcpAdminController : ApiAdminControllerBase
{
    private readonly IMcpServerHost _mcpServerHost;
    private readonly IOptionsMonitor<McpServerOptions> _mcpOptions;

    public DefaultMcpAdminController(
        IMcpServerHost mcpServerHost,
        IOptionsMonitor<McpServerOptions> mcpOptions)
    {
        _mcpServerHost = Check.NotNull(mcpServerHost);
        _mcpOptions = Check.NotNull(mcpOptions);
    }

    /// <summary>
    /// 获取 MCP Server 状态
    /// </summary>
    [HttpGet("status")]
    public virtual async Task<ApiResult<McpServerStatusDto>> GetStatus()
    {
        var options = _mcpOptions.CurrentValue;
        var tools = await _mcpServerHost.ListToolsAsync();

        return ApiResult<McpServerStatusDto>.Ok(new McpServerStatusDto
        {
            Enabled = options.Enabled,
            Endpoint = options.Endpoint,
            RequireAuthentication = options.RequireAuthentication,
            RateLimitPerMinute = options.RateLimitPerMinute,
            ExposedAgentCount = _mcpServerHost.GetExposedAgentIds().Count,
            CustomToolCount = _mcpServerHost.GetCustomToolNames().Count,
            TotalToolCount = tools.Count
        });
    }

    /// <summary>
    /// 获取已注册的 MCP 工具列表
    /// </summary>
    [HttpGet("tools")]
    public virtual async Task<ApiResult<List<McpToolInfoDto>>> GetTools()
    {
        var tools = await _mcpServerHost.ListToolsAsync();
        var dtos = tools.Select(t => new McpToolInfoDto
        {
            Name = t.Name,
            Description = t.Description
        }).ToList();

        return ApiResult<List<McpToolInfoDto>>.Ok(dtos);
    }

    /// <summary>
    /// 获取已暴露的 Agent ID 列表
    /// </summary>
    [HttpGet("agents")]
    public virtual ApiResult<List<Guid>> GetExposedAgents()
    {
        var agentIds = _mcpServerHost.GetExposedAgentIds().ToList();
        return ApiResult<List<Guid>>.Ok(agentIds);
    }

    /// <summary>
    /// 动态暴露 Agent 为 MCP 工具
    /// </summary>
    /// <remarks>
    /// Agent 必须对调用方可见且已启用（否则 404 / 400，不写入）。暴露只存在于本进程：重启后只有
    /// <c>AI:McpServer:ExposedAgentIds</c> 里列出的会恢复，多实例之间也不共享。
    /// </remarks>
    [HttpPost("agents/{agentId:guid}/expose")]
    [ApiAuthorize(PermissionName = "ai.mcpServer.update")]
    public virtual async Task<ApiResult> ExposeAgent(
        Guid agentId, [FromBody] McpToolExposureOptions? options = null, CancellationToken ct = default)
        => (await _mcpServerHost.ExposeAgentAsync(agentId, options, ct)).ToApiResult();

    /// <summary>
    /// 移除已暴露的 Agent
    /// </summary>
    /// <remarks>
    /// 列在 <c>AI:McpServer:ExposedAgentIds</c> 里的 Agent，移除维持到进程重启，回执里会说明；永久下线要同时改配置。
    /// </remarks>
    [HttpDelete("agents/{agentId:guid}")]
    [ApiAuthorize(PermissionName = "ai.mcpServer.update")]
    public virtual ApiResult RemoveAgent(Guid agentId)
    {
        var removed = _mcpServerHost.RemoveAgent(agentId);
        if (!removed)
            return ApiResult.Error("Agent not found in exposed list.", 404);

        return _mcpServerHost.IsConfiguredAgent(agentId)
            ? ApiResult.Ok("Removed until the next restart: this agent is listed in AI:McpServer:ExposedAgentIds, remove it there to stop exposing it for good.")
            : ApiResult.Ok();
    }
}
