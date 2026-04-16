using Microsoft.AspNetCore.Mvc;

namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// MCP Server 管理控制器 — 查看服务器状态、工具列表、动态管理暴露的 Agent
/// </summary>
[DefaultController]
[Route("admin/mcp")]
[ApiExplorerSettings(GroupName = "admin")]
public class DefaultMcpAdminController : ApiAdminControllerBase
{
    private readonly IMcpServerHost _mcpServerHost;
    private readonly IOptions<McpServerOptions> _mcpOptions;
    private readonly IMcpServerRegistryService _registryService;

    public DefaultMcpAdminController(
        IMcpServerHost mcpServerHost,
        IOptions<McpServerOptions> mcpOptions,
        IMcpServerRegistryService registryService)
    {
        _mcpServerHost = Check.NotNull(mcpServerHost);
        _mcpOptions = Check.NotNull(mcpOptions);
        _registryService = Check.NotNull(registryService);
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
            TenantIsolation = options.TenantIsolation,
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

    // ---- Entity-driven MCP Server Registration CRUD (Phase 5 backend prereq) ----
    //
    // 这些端点管理外部 MCP Server 的客户端注册条目（database-driven catalogue），
    // 与上面的 server-hosting 端点（status / tools / agents）完全独立。
    // 凭证以 IDataProtectionProvider 加密形式存储；DTO 仅暴露 HasAuthToken 布尔标志。
    // 注意：MCP 运行时连接路径目前仍读取 McpServerOptions 配置，实体 → 运行时绑定为后续工作。

    /// <summary>
    /// 分页查询 MCP Server 注册列表
    /// </summary>
    [HttpPost("entities/query")]
    public virtual async Task<ApiResult<IPagedList<McpServerRegistrationDto>>> GetList([FromBody] McpServerRegistrationQueryDto query)
    {
        var result = await _registryService.GetPagedListAsync(query, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 按 ID 获取 MCP Server 注册
    /// </summary>
    [HttpGet("entities/{id:guid}")]
    public virtual async Task<ApiResult<McpServerRegistrationDto>> GetById(Guid id)
    {
        var result = await _registryService.GetByIdAsync(id, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建 MCP Server 注册
    /// </summary>
    [HttpPost("entities")]
    public virtual async Task<ApiResult<McpServerRegistrationDto>> Create([FromBody] CreateMcpServerRegistrationDto dto)
    {
        var result = await _registryService.CreateAsync(dto, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新 MCP Server 注册
    /// </summary>
    [HttpPut("entities/{id:guid}")]
    public virtual async Task<ApiResult<McpServerRegistrationDto>> Update(Guid id, [FromBody] UpdateMcpServerRegistrationDto dto)
    {
        var result = await _registryService.UpdateAsync(id, dto, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 软删除 MCP Server 注册
    /// </summary>
    [HttpDelete("entities/{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _registryService.DeleteAsync(id, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 测试 MCP Server 连接（轻量探针：校验 IsEnabled + URI 合法 + 凭证可解密）
    /// </summary>
    [HttpPost("entities/{id:guid}/test")]
    public virtual async Task<ApiResult<McpServerTestResultDto>> TestConnection(Guid id)
    {
        var result = await _registryService.TestConnectionAsync(id, HttpContext.RequestAborted);
        return result.ToApiResult();
    }
}
