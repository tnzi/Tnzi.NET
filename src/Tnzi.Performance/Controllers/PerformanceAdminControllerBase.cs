
namespace Tnzi.Performance.Controllers;

/// <summary>
/// Performance monitoring admin controller base class.
/// Provides endpoints for viewing performance percentiles, endpoint statistics,
/// slow request tracking, and clearing collected data.
/// </summary>
[Route("admin/performance")]
[ApiExplorerSettings(GroupName = "diagnostics-admin")]
public abstract class PerformanceAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IPerformanceCollector PerformanceCollector;

    /// <summary>
    /// Initialize the performance admin controller
    /// </summary>
    protected PerformanceAdminControllerBase(IPerformanceCollector performanceCollector)
    {
        PerformanceCollector = Check.NotNull(performanceCollector);
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
    /// Clear all collected performance data
    /// </summary>
    [HttpDelete]
    public virtual ApiResult Clear()
    {
        PerformanceCollector.Clear();
        return ApiResult.Ok("Performance data cleared");
    }
}
