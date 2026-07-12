
namespace Tnzi.System.Controllers.Admin;

/// <summary>
/// 访问日志管理控制器
/// 提供访问日志查询、统计、删除过期记录等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/access-logs")]
[ApiAuthorize(PermissionName = "system.accessLog.view")]
public class DefaultAccessLogAdminController : ApiAdminControllerBase
{
    protected readonly IAccessLogService AccessLogService;

    /// <summary>
    /// 初始化访问日志管理控制器
    /// </summary>
    public DefaultAccessLogAdminController(IAccessLogService accessLogService)
    {
        AccessLogService = Check.NotNull(accessLogService);
    }

    /// <summary>
    /// 获取访问日志（分页）
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<AccessLogDto>>> GetAccessLogs([FromQuery] AccessLogQueryDto query)
    {
        var result = await AccessLogService.GetAccessLogsAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据ID获取访问日志
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<AccessLogDto>> GetById(Guid id)
    {
        var result = await AccessLogService.GetAccessLogByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 记录访问日志
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "system.accessLog.create")]
    public virtual async Task<ApiResult> LogAccess([FromBody] AccessLogDto log)
    {
        var result = await AccessLogService.LogAccessAsync(log);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取访问日志统计
    /// </summary>
    [HttpGet("statistics")]
    public virtual async Task<ApiResult<AccessLogStatisticsDto>> GetAccessLogStatistics(
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null)
    {
        var result = await AccessLogService.GetAccessLogStatisticsAsync(startTime, endTime);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除过期访问日志
    /// </summary>
    [HttpDelete("expired")]
    [ApiAuthorize(PermissionName = "system.accessLog.delete")]
    public virtual async Task<ApiResult> DeleteExpiredAccessLogs([FromQuery] int days = 90)
    {
        var result = await AccessLogService.DeleteExpiredAccessLogsAsync(days);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除访问日志
    /// </summary>
    [HttpDelete("batch")]
    [ApiAuthorize(PermissionName = "system.accessLog.delete")]
    public virtual async Task<ApiResult> DeleteAccessLogs([FromBody] IEnumerable<Guid> ids)
    {
        var result = await AccessLogService.DeleteAccessLogsAsync(ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取访问日志趋势统计（按天/周/月）
    /// </summary>
    [HttpGet("statistics/trend")]
    public virtual async Task<ApiResult<AccessLogTrendDto>> GetAccessLogTrend(
        [FromQuery] AccessLogTrendInterval interval = AccessLogTrendInterval.Daily,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var end = endDate ?? DateTime.UtcNow;
        var start = startDate ?? end.AddDays(-30);
        var result = await AccessLogService.GetAccessLogTrendAsync(interval, start, end);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取 Top 端点统计（最热/最慢/最多错误）
    /// </summary>
    [HttpGet("statistics/top-endpoints")]
    public virtual async Task<ApiResult<List<TopEndpointDto>>> GetTopEndpoints(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int top = 10,
        [FromQuery] TopEndpointSortBy sortBy = TopEndpointSortBy.Hits)
    {
        var result = await AccessLogService.GetTopEndpointsAsync(startDate, endDate, top, sortBy);
        return result.ToApiResult();
    }
}
