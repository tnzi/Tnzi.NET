namespace Tnzi.AI.Mcp.Controllers.Admin;

/// <summary>
/// MCP 工具使用分析管理控制器
/// </summary>
[DefaultController]
[Route("admin/ai/mcp/tool-analytics")]
[ApiExplorerSettings(GroupName = "admin")]
public class DefaultMcpToolAnalyticsAdminController : ApiAdminControllerBase
{
    private readonly IMcpToolAnalyticsService _analyticsService;

    public DefaultMcpToolAnalyticsAdminController(IMcpToolAnalyticsService analyticsService)
    {
        _analyticsService = Check.NotNull(analyticsService);
    }

    /// <summary>
    /// 获取指定工具的统计信息
    /// </summary>
    [HttpGet("stats/{toolName}")]
    public virtual async Task<ApiResult<McpToolStatsDto>> GetStats(string toolName, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        var result = await _analyticsService.GetToolStatsAsync(toolName, from, to);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取最常用工具排名
    /// </summary>
    [HttpGet("most-used")]
    public virtual async Task<ApiResult<List<McpToolPopularityDto>>> GetMostUsed([FromQuery][Range(1, 100)] int top = 10, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        var result = await _analyticsService.GetMostUsedToolsAsync(top, from, to);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取指定工具的错误列表
    /// </summary>
    [HttpGet("errors/{toolName}")]
    public virtual async Task<ApiResult<List<McpToolErrorDto>>> GetErrors(string toolName, [FromQuery][Range(1, 100)] int top = 20)
    {
        var result = await _analyticsService.GetToolErrorsAsync(toolName, top);
        return result.ToApiResult();
    }

    /// <summary>
    /// 清理过期记录
    /// </summary>
    [HttpDelete("cleanup")]
    public virtual async Task<ApiResult<int>> Cleanup([FromQuery] int retentionDays = 90)
    {
        var result = await _analyticsService.CleanupOldRecordsAsync(retentionDays);
        return result.ToApiResult();
    }
}
