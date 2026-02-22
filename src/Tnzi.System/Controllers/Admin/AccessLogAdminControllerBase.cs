
namespace Tnzi.System.Controllers.Admin;

/// <summary>
/// 访问日志控制器基类
/// 提供访问日志查询、统计、删除过期记录等API端点，所有方法支持重写
/// </summary>
[Route("admin/access-logs")]
public abstract class AccessLogAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IAccessLogService AccessLogService;

    /// <summary>
    /// 初始化访问日志控制器基类
    /// </summary>
    protected AccessLogAdminControllerBase(IAccessLogService accessLogService)
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
    public virtual async Task<ApiResult> DeleteExpiredAccessLogs([FromQuery] int days = 90)
    {
        var result = await AccessLogService.DeleteExpiredAccessLogsAsync(days);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除访问日志
    /// </summary>
    [HttpDelete("batch")]
    public virtual async Task<ApiResult> DeleteAccessLogs([FromBody] IEnumerable<Guid> ids)
    {
        var result = await AccessLogService.DeleteAccessLogsAsync(ids);
        return result.ToApiResult();
    }
}
