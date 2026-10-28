
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
    protected readonly IOptionsMonitor<PerformanceOptions> PerformanceOptionsMonitor;

    /// <summary>
    /// Initialize the performance admin controller
    /// </summary>
    public DefaultPerformanceAdminController(
        IPerformanceCollector performanceCollector,
        IPerformanceMonitorService performanceMonitor,
        IOptionsMonitor<PerformanceOptions> performanceOptions)
    {
        PerformanceCollector = Check.NotNull(performanceCollector);
        PerformanceMonitor = Check.NotNull(performanceMonitor);
        PerformanceOptionsMonitor = Check.NotNull(performanceOptions);
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
    /// Get recent slow request records, slowest first
    /// </summary>
    /// <param name="count">Maximum number of records (default: 20)</param>
    /// <param name="thresholdMs">
    /// Duration threshold in milliseconds. When omitted, the configured
    /// <c>Performance:SlowRequestThresholdMs</c> applies (read per request, so a change in the settings
    /// center takes effect at once); when that is not configured either, the slowest requests are returned.
    /// </param>
    [HttpGet("slow-requests")]
    public virtual ApiResult<List<SlowRequestRecord>> GetSlowRequests([FromQuery] int count = 20, [FromQuery] double? thresholdMs = null)
    {
        var threshold = thresholdMs ?? PerformanceOptionsMonitor.CurrentValue.SlowRequestThresholdMs;
        var result = PerformanceCollector.GetSlowRequestDetails(count, threshold);
        return ApiResult<List<SlowRequestRecord>>.Ok(result);
    }

    /// <summary>
    /// Get the process-wide runtime counters collected by the core
    /// <see cref="IPerformanceMonitorService"/>: database query volume, slow-query count,
    /// average query duration and cache hit rate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与本模块自己采集的 HTTP 请求指标互补：那边是"每个端点多慢"，这边是"慢在数据库还是缓存"。
    /// </para>
    /// <para>
    /// ★ 统计口径不是"进程内的全部往返"，读数字之前要知道它数了什么：
    /// 数据库计数只来自仓储的 <c>ToListAsync</c> 与工作单元的 <c>SaveChanges</c>（慢查询只来自后者），
    /// 服务里直接对 <c>IQueryable</c> 调 EF 的 <c>FirstOrDefaultAsync</c> / <c>CountAsync</c> / <c>ToListAsync</c> 不经过这两处、不计入；
    /// 缓存命中与未命中来自框架自带的两个 <c>ICache</c> 实现（内存与 Redis）的读路径，自定义实现自己不记就不计入。
    /// 适合看趋势与命中率，不适合当作数据库往返总数。
    /// </para>
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
