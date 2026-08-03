
namespace Tnzi.Performance.Controllers;

/// <summary>
/// Default performance monitoring admin controller (activated by HostingModule via
/// <c>[DefaultController]</c>; applications override it by registering a controller
/// on the same route). Provides endpoints for viewing performance percentiles,
/// endpoint statistics, slow request tracking, and clearing collected data.
/// </summary>
[Route("admin/performance")]
[DefaultController]
[ApiAuthorize(PermissionName = "system.performance.view")]
public class DefaultPerformanceAdminController : ApiAdminControllerBase
{
    protected readonly IPerformanceCollector PerformanceCollector;
    protected readonly IPerformanceMonitorService PerformanceMonitor;

    /// <summary>
    /// Initialize the performance admin controller
    /// </summary>
    public DefaultPerformanceAdminController(
        IPerformanceCollector performanceCollector,
        IPerformanceMonitorService performanceMonitor)
    {
        PerformanceCollector = Check.NotNull(performanceCollector);
        PerformanceMonitor = Check.NotNull(performanceMonitor);
    }

    /// <summary>
    /// Get overall performance percentile summary (P50/P95/P99)
    /// </summary>
    /// <param name="minutes">Time window in minutes (default: 60)</param>
    [HttpGet("summary")]
    public virtual ApiResult<PercentileResult> GetSummary([FromQuery] int minutes = 60)
    {
        var timeWindow = minutes > 0 ? TimeSpan.FromMinutes(minutes) : (TimeSpan?)null;
        var result = PerformanceCollector.GetPercentiles(timeWindow);
        return ApiResult<PercentileResult>.Ok(result);
    }

    /// <summary>
    /// Get per-endpoint performance statistics
    /// </summary>
    /// <param name="minutes">Time window in minutes (default: 60)</param>
    /// <param name="topN">Maximum number of endpoints to return (0 = all)</param>
    [HttpGet("endpoints")]
    public virtual ApiResult<List<EndpointStats>> GetEndpoints([FromQuery] int minutes = 60, [FromQuery] int topN = 0)
    {
        var timeWindow = minutes > 0 ? TimeSpan.FromMinutes(minutes) : (TimeSpan?)null;
        var result = PerformanceCollector.GetEndpointStats(timeWindow, topN);
        return ApiResult<List<EndpointStats>>.Ok(result);
    }

    /// <summary>
    /// Get recent slow request records
    /// </summary>
    /// <param name="count">Maximum number of records (default: 20)</param>
    /// <param name="thresholdMs">Duration threshold in milliseconds (optional)</param>
    [HttpGet("slow-requests")]
    public virtual ApiResult<List<SlowRequestRecord>> GetSlowRequests([FromQuery] int count = 20, [FromQuery] double? thresholdMs = null)
    {
        var result = PerformanceCollector.GetSlowRequestDetails(count, thresholdMs);
        return ApiResult<List<SlowRequestRecord>>.Ok(result);
    }

    /// <summary>
    /// Get the process-wide runtime counters collected by the core
    /// <see cref="IPerformanceMonitorService"/>: database query volume, slow-query count,
    /// average query duration and cache hit rate.
    /// </summary>
    /// <remarks>
    /// 这些计数由 <c>EfCoreRepository</c> / <c>UnitOfWorkManager</c> / <c>MemoryCacheService</c>
    /// 一直在写，但在此之前**没有任何读取端** —— 采集了却没人看。本端点把它接出来，
    /// 与本模块自己采集的 HTTP 请求指标互补：那边是"每个端点多慢"，这边是"慢在数据库还是缓存"。
    /// </remarks>
    [HttpGet("runtime")]
    public virtual ApiResult<PerformanceSnapshot> GetRuntimeCounters()
    {
        return ApiResult<PerformanceSnapshot>.Ok(PerformanceMonitor.GetSnapshot());
    }

    /// <summary>
    /// Clear all collected performance data
    /// </summary>
    [HttpDelete]
    public virtual ApiResult Clear()
    {
        PerformanceCollector.Clear();
        return ApiResult.Ok("Performance data cleared");
    }
}
