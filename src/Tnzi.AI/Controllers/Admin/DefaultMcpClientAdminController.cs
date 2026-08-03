
namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// MCP Client 管理控制器 - 管理外部 MCP Server 注册条目（client-side catalogue）
/// </summary>
/// <remarks>
/// 这些端点管理 Tnzi 作为 MCP Client 连接外部 MCP Server 的注册信息，
/// 与 <c>DefaultMcpAdminController</c>（管理 Tnzi 自身暴露为 MCP Server）完全独立。
/// 凭证以 IDataProtectionProvider 加密形式存储；DTO 仅暴露 HasAuthToken 布尔标志。
/// </remarks>
[DefaultController]
[Route("admin/mcp/client")]
[ApiExplorerSettings(GroupName = "admin")]
[ApiAuthorize(PermissionName = "ai.mcp.view")]
public class DefaultMcpClientAdminController : ApiAdminControllerBase
{
    private readonly IMcpServerRegistryService _registryService;

    public DefaultMcpClientAdminController(IMcpServerRegistryService registryService)
    {
        _registryService = Check.NotNull(registryService);
    }

    /// <summary>
    /// 分页查询外部 MCP Server 注册列表
    /// </summary>
    [HttpPost("servers/query")]
    public virtual async Task<ApiResult<IPagedList<McpServerRegistrationDto>>> GetList([FromBody] McpServerRegistrationQueryDto query)
    {
        var result = await _registryService.GetPagedListAsync(query, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 按 ID 获取外部 MCP Server 注册
    /// </summary>
    [HttpGet("servers/{id:guid}")]
    public virtual async Task<ApiResult<McpServerRegistrationDto>> GetById(Guid id)
    {
        var result = await _registryService.GetByIdAsync(id, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建外部 MCP Server 注册
    /// </summary>
    [HttpPost("servers")]
    [ApiAuthorize(PermissionName = "ai.mcp.create")]
    public virtual async Task<ApiResult<McpServerRegistrationDto>> Create([FromBody] CreateMcpServerRegistrationDto dto)
    {
        var result = await _registryService.CreateAsync(dto, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新外部 MCP Server 注册
    /// </summary>
    [HttpPut("servers/{id:guid}")]
    [ApiAuthorize(PermissionName = "ai.mcp.update")]
    public virtual async Task<ApiResult<McpServerRegistrationDto>> Update(Guid id, [FromBody] UpdateMcpServerRegistrationDto dto)
    {
        var result = await _registryService.UpdateAsync(id, dto, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 软删除外部 MCP Server 注册
    /// </summary>
    [HttpDelete("servers/{id:guid}")]
    [ApiAuthorize(PermissionName = "ai.mcp.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _registryService.DeleteAsync(id, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// 测试外部 MCP Server 连接（轻量探针：校验 IsEnabled + URI 合法 + 凭证可解密）
    /// </summary>
    [HttpPost("servers/{id:guid}/test")]
    [ApiAuthorize(PermissionName = "ai.mcp.execute")]
    public virtual async Task<ApiResult<McpServerTestResultDto>> TestConnection(Guid id)
    {
        var result = await _registryService.TestConnectionAsync(id, HttpContext.RequestAborted);
        return result.ToApiResult();
    }
}
