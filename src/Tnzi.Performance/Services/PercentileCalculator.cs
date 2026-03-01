
namespace Tnzi.Performance.Services;

/// <summary>
/// Sort-based percentile calculator for request duration metrics.
/// Uses the nearest-rank method for percentile computation.
/// </summary>
public static class PercentileCalculator
{
    /// <summary>
    /// Calculate percentile statistics from performance metrics
    /// </summary>
    /// <param name="records">Performance metric records</param>
    /// <param name="timeWindow">Optional time window filter</param>
    /// <returns>Percentile result with P50/P95/P99, average, min, max</returns>
    public static PercentileResult Calculate(IEnumerable<PerformanceMetrics> records, TimeSpan? timeWindow = null)
    {
        Check.NotNull(records);

        var filtered = timeWindow.HasValue
            ? records.Where(r => r.Timestamp >= DateTime.UtcNow.Subtract(timeWindow.Value))
            : records;

        var durations = filtered.Select(r => r.DurationMs).ToList();

        if (durations.Count == 0)
        {
            return new PercentileResult(
                P50: 0, P95: 0, P99: 0,
                Average: 0, Min: 0, Max: 0,
                SampleCount: 0);
        }

        durations.Sort();

        return new PercentileResult(
            P50: GetPercentile(durations, 50),
            P95: GetPercentile(durations, 95),
            P99: GetPercentile(durations, 99),
            Average: durations.Average(),
            Min: durations[0],
            Max: durations[^1],
            SampleCount: durations.Count);
    }

    /// <summary>
    /// Calculate a specific percentile value using the nearest-rank method
    /// </summary>
    /// <param name="sortedValues">Pre-sorted list of values</param>
    /// <param name="percentile">Percentile to compute (0-100)</param>
    /// <returns>The percentile value</returns>
    public static double GetPercentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0;
        if (sortedValues.Count == 1) return sortedValues[0];

        // Use linear interpolation for more accurate percentile estimation
        var rank = (percentile / 100.0) * (sortedValues.Count - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);

        if (lowerIndex == upperIndex)
            return sortedValues[lowerIndex];

        var fraction = rank - lowerIndex;
        return sortedValues[lowerIndex] + fraction * (sortedValues[upperIndex] - sortedValues[lowerIndex]);
    }
}
