using Microsoft.AspNetCore.Mvc;

namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// MCP Server 管理控制器 — 查看服务器状态、工具列表、动态管理暴露的 Agent
/// </summary>
/// <remarks>
/// 仅管理 Tnzi 自身作为 MCP Server 的配置（status/tools/agents）。
/// 外部 MCP Server 注册条目的 CRUD 由 <c>DefaultMcpClientAdminController</c>（AIMcpClientModule）负责。
/// </remarks>
[DefaultController]
[Route("admin/mcp")]
[ApiExplorerSettings(GroupName = "admin")]
public class DefaultMcpAdminController : ApiAdminControllerBase
{
    private readonly IMcpServerHost _mcpServerHost;
    private readonly IOptions<McpServerOptions> _mcpOptions;

    public DefaultMcpAdminController(
        IMcpServerHost mcpServerHost,
        IOptions<McpServerOptions> mcpOptions)
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
        var options = _mcpOptions.Value;
        var tools = await _mcpServerHost.ListToolsAsync();

        return ApiResult<McpServerStatusDto>.Ok(new McpServerStatusDto
        {
            Enabled = options.Enabled,
            Transport = options.Transport,
            Endpoint = options.Endpoint,
            RequireAuthentication = options.RequireAuthentication,
            RateLimitPerTenant = options.RateLimitPerTenant,
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
    [HttpPost("agents/{agentId:guid}/expose")]
    public virtual ApiResult ExposeAgent(Guid agentId, [FromBody] McpToolExposureOptions? options = null)
    {
        _mcpServerHost.ExposeAgent(agentId, options);
        return ApiResult.Ok();
    }

    /// <summary>
    /// 移除已暴露的 Agent
    /// </summary>
    [HttpDelete("agents/{agentId:guid}")]
    public virtual ApiResult RemoveAgent(Guid agentId)
    {
        var removed = _mcpServerHost.RemoveAgent(agentId);
        if (!removed)
            return ApiResult.Error("Agent not found in exposed list.", 404);

        return ApiResult.Ok();
    }
}
