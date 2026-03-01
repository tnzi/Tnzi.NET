
namespace Tnzi.Performance.Services;

/// <summary>
/// Calculator for per-endpoint aggregated performance statistics
/// </summary>
public static class EndpointStatsCalculator
{
    /// <summary>
    /// Calculate per-endpoint statistics from performance metrics
    /// </summary>
    /// <param name="records">Performance metric records</param>
    /// <param name="timeWindow">Optional time window filter</param>
    /// <param name="topN">Maximum number of endpoints to return (0 = all)</param>
    /// <returns>Per-endpoint statistics sorted by request count descending</returns>
    public static List<EndpointStats> Calculate(IEnumerable<PerformanceMetrics> records, TimeSpan? timeWindow = null, int topN = 0)
    {
        Check.NotNull(records);

        var filtered = timeWindow.HasValue
            ? records.Where(r => r.Timestamp >= DateTime.UtcNow.Subtract(timeWindow.Value))
            : records;

        var groups = filtered
            .GroupBy(r => new { r.Path, r.Method })
            .Select(g =>
            {
                var durations = g.Select(r => r.DurationMs).OrderBy(d => d).ToList();
                return new EndpointStats(
                    Path: g.Key.Path,
                    Method: g.Key.Method,
                    RequestCount: g.Count(),
                    AverageDurationMs: Math.Round(durations.Average(), 2),
                    P95DurationMs: Math.Round(PercentileCalculator.GetPercentile(durations, 95), 2),
                    MinDurationMs: durations[0],
                    MaxDurationMs: durations[^1],
                    ErrorCount: g.Count(r => r.StatusCode >= 400),
                    LastRequestTime: g.Max(r => r.Timestamp));
            })
            .OrderByDescending(s => s.RequestCount)
            .AsEnumerable();

        if (topN > 0)
        {
            groups = groups.Take(topN);
        }

        return groups.ToList();
    }
}
