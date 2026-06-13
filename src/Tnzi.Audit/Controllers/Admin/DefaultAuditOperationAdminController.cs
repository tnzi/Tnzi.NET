namespace Tnzi.Audit.Controllers.Admin;

/// <summary>
/// 操作审计管理控制器
/// 提供操作审计查询、统计信息等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/audit-operations")]
public class DefaultAuditOperationAdminController : ApiAdminControllerBase
{
    protected readonly IAuditOperationService AuditOperationService;

    /// <summary>
    /// 初始化操作审计管理控制器
    /// </summary>
    /// <param name="auditOperationService">操作审计服务</param>
    public DefaultAuditOperationAdminController(IAuditOperationService auditOperationService)
    {
        AuditOperationService = Check.NotNull(auditOperationService);
    }

    /// <summary>
    /// 根据ID获取操作审计
    /// </summary>
    /// <param name="id">操作审计ID</param>
    /// <returns>操作审计信息</returns>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<AuditOperationDto>> GetById(Guid id)
    {
        var result = await AuditOperationService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取操作审计列表（分页）
    /// </summary>
    /// <param name="query">查询条件</param>
    /// <returns>分页的操作审计列表</returns>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<AuditOperationDto>>> GetList([FromBody] AuditOperationQueryDto query)
    {
        var result = await AuditOperationService.GetOperationsAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取用户的操作审计列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <param name="resultType">结果类型（可选）</param>
    /// <returns>操作审计列表</returns>
    [HttpGet("user/{userId:guid}")]
    public virtual async Task<ApiResult<IEnumerable<AuditOperationDto>>> GetUserOperations(
        Guid userId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] AuditResultType? resultType = null)
    {
        var result = await AuditOperationService.GetUserOperationsAsync(userId, startDate, endDate, resultType);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取功能的操作统计
    /// </summary>
    /// <param name="functionName">功能名称</param>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <returns>操作统计信息</returns>
    [HttpGet("statistics/function/{functionName}")]
    public virtual async Task<ApiResult<AuditOperationStatistics>> GetFunctionStatistics(
        string functionName,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var result = await AuditOperationService.GetFunctionStatisticsAsync(functionName, startDate, endDate);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取用户的操作统计
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <returns>操作统计信息</returns>
    [HttpGet("statistics/user/{userId:guid}")]
    public virtual async Task<ApiResult<AuditOperationStatistics>> GetUserStatistics(
        Guid userId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var result = await AuditOperationService.GetUserStatisticsAsync(userId, startDate, endDate);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除过期操作审计
    /// </summary>
    /// <param name="days">保留天数；缺省时使用配置中心的 Audit:RetentionDays（热读）</param>
    /// <returns>删除的记录数</returns>
    [HttpDelete("expired")]
    public virtual async Task<ApiResult<int>> DeleteExpired([FromQuery] int? days = null)
    {
        var result = await AuditOperationService.DeleteExpiredOperationsAsync(days);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取审计操作趋势统计
    /// </summary>
    [HttpGet("trend")]
    public virtual async Task<ApiResult<List<AuditTrendPointDto>>> GetTrend(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] AuditTrendGroupBy groupBy = AuditTrendGroupBy.Daily)
    {
        var result = await AuditOperationService.GetAuditTrendAsync(startDate, endDate, groupBy);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取 Top N 功能统计
    /// </summary>
    [HttpGet("top-functions")]
    public virtual async Task<ApiResult<List<TopFunctionDto>>> GetTopFunctions(
        [FromQuery] int topN = 10,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var result = await AuditOperationService.GetTopFunctionsAsync(topN, startDate, endDate);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取 Top N 活跃用户统计
    /// </summary>
    [HttpGet("top-users")]
    public virtual async Task<ApiResult<List<TopUserDto>>> GetTopUsers(
        [FromQuery] int topN = 10,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var result = await AuditOperationService.GetTopUsersAsync(topN, startDate, endDate);
        return result.ToApiResult();
    }

    /// <summary>
    /// Export audit operations as CSV
    /// </summary>
    /// <param name="query">Query filter criteria</param>
    /// <returns>CSV file download</returns>
    [HttpPost("export/csv")]
    public virtual async Task<IActionResult> ExportCsv([FromBody] AuditOperationQueryDto query)
    {
        var result = await AuditOperationService.ExportToCsvAsync(query);
        if (!result.Succeeded)
        {
            return new BadRequestObjectResult(result.Message);
        }

        var bytes = Encoding.UTF8.GetBytes(result.Data!);
        var fileName = $"audit_export_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return File(bytes, "text/csv", fileName);
    }

    /// <summary>
    /// Export audit operations as JSON
    /// </summary>
    /// <param name="query">Query filter criteria</param>
    /// <returns>JSON file download</returns>
    [HttpPost("export/json")]
    public virtual async Task<IActionResult> ExportJson([FromBody] AuditOperationQueryDto query)
    {
        var result = await AuditOperationService.ExportToJsonAsync(query);
        if (!result.Succeeded)
        {
            return new BadRequestObjectResult(result.Message);
        }

        var bytes = Encoding.UTF8.GetBytes(result.Data!);
        var fileName = $"audit_export_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
        return File(bytes, "application/json", fileName);
    }
}
