
namespace Tnzi.Performance.Services;

/// <summary>
/// Performance collector interface for recording and analyzing request metrics
/// </summary>
public interface IPerformanceCollector
{
    /// <summary>
    /// Record a performance metric entry
    /// </summary>
    /// <param name="metrics">Performance metrics</param>
    void Record(PerformanceMetrics metrics);

    /// <summary>
    /// Get performance statistics within a time window
    /// </summary>
    /// <param name="timeWindow">Time window</param>
    /// <param name="slowRequestThresholdMs">Slow request threshold in milliseconds</param>
    /// <returns>Performance statistics</returns>
    PerformanceStats GetStats(TimeSpan timeWindow, double? slowRequestThresholdMs = null);

    /// <summary>
    /// Get recent performance records
    /// </summary>
    /// <param name="count">Number of records</param>
    /// <returns>Recent performance records</returns>
    List<PerformanceMetrics> GetRecentRecords(int count);

    /// <summary>
    /// Get slow request records exceeding the threshold
    /// </summary>
    /// <param name="thresholdMs">Threshold in milliseconds</param>
    /// <param name="count">Maximum number of records to return</param>
    /// <returns>Slow request records sorted by duration descending</returns>
    List<PerformanceMetrics> GetSlowRequests(double thresholdMs, int count);

    /// <summary>
    /// Clear all collected records
    /// </summary>
    void Clear();

    /// <summary>
    /// Calculate percentile statistics (P50/P95/P99) for all collected request durations
    /// </summary>
    /// <param name="timeWindow">Optional time window to filter records. If null, uses all records.</param>
    /// <returns>Percentile calculation result</returns>
    PercentileResult GetPercentiles(TimeSpan? timeWindow = null)
    {
        // Default implementation uses sort-based percentile calculation
        return PercentileCalculator.Calculate(GetRecentRecords(int.MaxValue), timeWindow);
    }

    /// <summary>
    /// Get per-endpoint aggregated statistics
    /// </summary>
    /// <param name="timeWindow">Optional time window to filter records. If null, uses all records.</param>
    /// <param name="topN">Maximum number of endpoints to return, sorted by request count descending. 0 returns all.</param>
    /// <returns>Per-endpoint statistics</returns>
    List<EndpointStats> GetEndpointStats(TimeSpan? timeWindow = null, int topN = 0)
    {
        // Default implementation aggregates from recent records
        return EndpointStatsCalculator.Calculate(GetRecentRecords(int.MaxValue), timeWindow, topN);
    }

    /// <summary>
    /// Get slow request records with detailed information
    /// </summary>
    /// <param name="count">Maximum number of records</param>
    /// <param name="thresholdMs">Optional threshold in milliseconds. If null, returns the slowest requests.</param>
    /// <returns>Slow request records sorted by duration descending</returns>
    List<SlowRequestRecord> GetSlowRequestDetails(int count, double? thresholdMs = null)
    {
        var threshold = thresholdMs ?? 0;
        return GetSlowRequests(threshold, count)
            .Select(m => new SlowRequestRecord(
                m.Path,
                m.Method,
                m.StatusCode,
                m.DurationMs,
                m.UserId,
                m.RequestId,
                m.Timestamp))
            .ToList();
    }
}
