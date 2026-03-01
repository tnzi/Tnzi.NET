
namespace Tnzi.Performance.Services;

/// <summary>
/// In-memory performance collector implementation.
/// Uses a ConcurrentQueue with bounded capacity for thread-safe metric collection.
/// </summary>
public class PerformanceCollector : IPerformanceCollector
{
    private readonly ConcurrentQueue<PerformanceMetrics> _recentRecords = new();
    private readonly int _maxHistorySize;

    /// <summary>
    /// Initialize the performance collector
    /// </summary>
    /// <param name="maxHistorySize">Maximum number of records to retain</param>
    public PerformanceCollector(int maxHistorySize = 1000)
    {
        _maxHistorySize = Check.GreaterThan(maxHistorySize, 0);
    }

    /// <inheritdoc />
    public void Record(PerformanceMetrics metrics)
    {
        Check.NotNull(metrics);
        _recentRecords.Enqueue(metrics);

        // Batch eviction to avoid contention under high concurrency
        var excessCount = _recentRecords.Count - _maxHistorySize;
        for (var i = 0; i < excessCount; i++)
        {
            _recentRecords.TryDequeue(out _);
        }
    }

    /// <inheritdoc />
    public PerformanceStats GetStats(TimeSpan timeWindow, double? slowRequestThresholdMs = null)
    {
        var cutoff = DateTime.UtcNow.Subtract(timeWindow);
        var relevantRecords = _recentRecords
            .Where(r => r.Timestamp >= cutoff)
            .ToList();

        if (relevantRecords.Count == 0)
        {
            return new PerformanceStats(
                TotalRequests: 0,
                AverageDurationMs: 0,
                MinDurationMs: 0,
                MaxDurationMs: 0,
                SlowRequestCount: 0,
                ByPath: new Dictionary<string, int>(),
                ByMethod: new Dictionary<string, int>(),
                ByStatusCode: new Dictionary<int, int>());
        }

        var durations = relevantRecords.Select(r => r.DurationMs).ToList();
        var slowThreshold = slowRequestThresholdMs ?? double.MaxValue;
        var slowRequestCount = durations.Count(d => d > slowThreshold);

        return new PerformanceStats(
            TotalRequests: relevantRecords.Count,
            AverageDurationMs: durations.Average(),
            MinDurationMs: durations.Min(),
            MaxDurationMs: durations.Max(),
            SlowRequestCount: slowRequestCount,
            ByPath: relevantRecords
                .GroupBy(r => r.Path)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByMethod: relevantRecords
                .GroupBy(r => r.Method)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByStatusCode: relevantRecords
                .GroupBy(r => r.StatusCode)
                .ToDictionary(g => g.Key, g => g.Count()));
    }

    /// <inheritdoc />
    public List<PerformanceMetrics> GetRecentRecords(int count)
    {
        return _recentRecords
            .OrderByDescending(r => r.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <inheritdoc />
    public List<PerformanceMetrics> GetSlowRequests(double thresholdMs, int count)
    {
        return _recentRecords
            .Where(r => r.DurationMs > thresholdMs)
            .OrderByDescending(r => r.DurationMs)
            .Take(count)
            .ToList();
    }

    /// <inheritdoc />
    public void Clear()
    {
        while (_recentRecords.TryDequeue(out _)) { }
    }

    /// <summary>
    /// Calculate percentile statistics directly from the internal queue (avoids extra copy)
    /// </summary>
    public PercentileResult GetPercentiles(TimeSpan? timeWindow = null)
    {
        return PercentileCalculator.Calculate(_recentRecords, timeWindow);
    }

    /// <summary>
    /// Calculate per-endpoint statistics directly from the internal queue
    /// </summary>
    public List<EndpointStats> GetEndpointStats(TimeSpan? timeWindow = null, int topN = 0)
    {
        return EndpointStatsCalculator.Calculate(_recentRecords, timeWindow, topN);
    }

    /// <summary>
    /// Get detailed slow request records
    /// </summary>
    public List<SlowRequestRecord> GetSlowRequestDetails(int count, double? thresholdMs = null)
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
