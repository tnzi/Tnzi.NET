using Tnzi.Performance.Models;
using Tnzi.Performance.Services;

namespace Tnzi.Performance.Tests;

public class PercentileCalculatorTests
{
    private static PerformanceMetrics CreateMetrics(double durationMs, DateTime? timestamp = null)
    {
        return new PerformanceMetrics(
            Path: "/api/test",
            Method: "GET",
            StatusCode: 200,
            DurationMs: durationMs,
            RequestSizeBytes: null,
            ResponseSizeBytes: null,
            UserId: null,
            RequestId: null,
            Timestamp: timestamp ?? DateTime.UtcNow);
    }

    [Fact]
    public void Calculate_EmptyRecords_ReturnsZeros()
    {
        var result = PercentileCalculator.Calculate([]);

        result.P50.ShouldBe(0);
        result.P95.ShouldBe(0);
        result.P99.ShouldBe(0);
        result.Average.ShouldBe(0);
        result.Min.ShouldBe(0);
        result.Max.ShouldBe(0);
        result.SampleCount.ShouldBe(0);
    }

    [Fact]
    public void Calculate_SingleRecord_AllPercentilesEqual()
    {
        var records = new[] { CreateMetrics(100) };
        var result = PercentileCalculator.Calculate(records);

        result.P50.ShouldBe(100);
        result.P95.ShouldBe(100);
        result.P99.ShouldBe(100);
        result.Average.ShouldBe(100);
        result.Min.ShouldBe(100);
        result.Max.ShouldBe(100);
        result.SampleCount.ShouldBe(1);
    }

    [Fact]
    public void Calculate_TwoRecords_CorrectPercentiles()
    {
        var records = new[] { CreateMetrics(100), CreateMetrics(200) };
        var result = PercentileCalculator.Calculate(records);

        result.Min.ShouldBe(100);
        result.Max.ShouldBe(200);
        result.Average.ShouldBe(150);
        result.SampleCount.ShouldBe(2);
        result.P50.ShouldBe(150); // Interpolated midpoint
    }

    [Fact]
    public void Calculate_100Records_CorrectPercentiles()
    {
        // Create 100 records with durations 1..100
        var records = Enumerable.Range(1, 100)
            .Select(i => CreateMetrics(i))
            .ToList();

        var result = PercentileCalculator.Calculate(records);

        result.SampleCount.ShouldBe(100);
        result.Min.ShouldBe(1);
        result.Max.ShouldBe(100);
        result.Average.ShouldBe(50.5);

        // P50 should be around 50.5 (interpolated)
        result.P50.ShouldBeInRange(49, 52);
        // P95 should be around 95
        result.P95.ShouldBeInRange(94, 96);
        // P99 should be around 99
        result.P99.ShouldBeInRange(98, 100);
    }

    [Fact]
    public void Calculate_WithTimeWindow_FiltersOldRecords()
    {
        var now = DateTime.UtcNow;
        var records = new[]
        {
            CreateMetrics(500, now.AddMinutes(-10)), // Old — outside window
            CreateMetrics(100, now.AddMinutes(-2)),  // Recent — inside window
            CreateMetrics(200, now.AddMinutes(-1)),  // Recent — inside window
        };

        var result = PercentileCalculator.Calculate(records, TimeSpan.FromMinutes(5));

        result.SampleCount.ShouldBe(2);
        result.Min.ShouldBe(100);
        result.Max.ShouldBe(200);
    }

    [Fact]
    public void Calculate_WithoutTimeWindow_IncludesAllRecords()
    {
        var now = DateTime.UtcNow;
        var records = new[]
        {
            CreateMetrics(500, now.AddHours(-24)),
            CreateMetrics(100, now.AddMinutes(-2)),
            CreateMetrics(200, now.AddMinutes(-1)),
        };

        var result = PercentileCalculator.Calculate(records);

        result.SampleCount.ShouldBe(3);
    }

    [Fact]
    public void Calculate_AllSameValues_PercentilesEqual()
    {
        var records = Enumerable.Range(1, 50)
            .Select(_ => CreateMetrics(42))
            .ToList();

        var result = PercentileCalculator.Calculate(records);

        result.P50.ShouldBe(42);
        result.P95.ShouldBe(42);
        result.P99.ShouldBe(42);
        result.Average.ShouldBe(42);
        result.Min.ShouldBe(42);
        result.Max.ShouldBe(42);
    }

    [Fact]
    public void Calculate_SkewedDistribution_HighPercentilesReflectOutliers()
    {
        // 95 fast requests + 5 slow requests (outliers)
        var records = new List<PerformanceMetrics>();
        for (var i = 0; i < 95; i++) records.Add(CreateMetrics(10));
        for (var i = 0; i < 5; i++) records.Add(CreateMetrics(1000));

        var result = PercentileCalculator.Calculate(records);

        result.SampleCount.ShouldBe(100);
        result.Min.ShouldBe(10);
        result.Max.ShouldBe(1000);
        // P50 should be 10 (most values are 10)
        result.P50.ShouldBe(10);
        // P95 should reflect the transition to outliers
        result.P95.ShouldBeGreaterThanOrEqualTo(10);
        // P99 should be close to 1000
        result.P99.ShouldBeGreaterThan(10);
    }

    [Fact]
    public void GetPercentile_InterpolationIsAccurate()
    {
        // Test the internal interpolation method directly
        var sorted = new List<double> { 10, 20, 30, 40, 50 };

        // P50 of [10,20,30,40,50] should be 30 (index 2)
        var p50 = PercentileCalculator.GetPercentile(sorted, 50);
        p50.ShouldBe(30);

        // P0 should be 10 (first element)
        var p0 = PercentileCalculator.GetPercentile(sorted, 0);
        p0.ShouldBe(10);

        // P100 should be 50 (last element)
        var p100 = PercentileCalculator.GetPercentile(sorted, 100);
        p100.ShouldBe(50);

        // P25 should be 20 (index 1)
        var p25 = PercentileCalculator.GetPercentile(sorted, 25);
        p25.ShouldBe(20);
    }

    [Fact]
    public void GetPercentile_EmptyList_ReturnsZero()
    {
        var result = PercentileCalculator.GetPercentile([], 50);
        result.ShouldBe(0);
    }
}
