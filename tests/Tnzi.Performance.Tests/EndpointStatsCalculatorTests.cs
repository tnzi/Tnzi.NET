using Tnzi.Performance.Models;
using Tnzi.Performance.Services;

namespace Tnzi.Performance.Tests;

public class EndpointStatsCalculatorTests
{
    private static PerformanceMetrics CreateMetrics(string path, string method, double durationMs, int statusCode = 200, DateTime? timestamp = null)
    {
        return new PerformanceMetrics(
            Path: path,
            Method: method,
            StatusCode: statusCode,
            DurationMs: durationMs,
            RequestSizeBytes: null,
            ResponseSizeBytes: null,
            UserId: null,
            RequestId: null,
            Timestamp: timestamp ?? DateTime.UtcNow);
    }

    [Fact]
    public void Calculate_EmptyRecords_ReturnsEmptyList()
    {
        var result = EndpointStatsCalculator.Calculate([]);
        result.ShouldBeEmpty();
    }

    [Fact]
    public void Calculate_SingleEndpoint_CorrectStats()
    {
        var records = new[]
        {
            CreateMetrics("/api/users", "GET", 100),
            CreateMetrics("/api/users", "GET", 200),
            CreateMetrics("/api/users", "GET", 150),
        };

        var result = EndpointStatsCalculator.Calculate(records);

        result.Count.ShouldBe(1);
        var stats = result[0];
        stats.Path.ShouldBe("/api/users");
        stats.Method.ShouldBe("GET");
        stats.RequestCount.ShouldBe(3);
        stats.AverageDurationMs.ShouldBe(150);
        stats.MinDurationMs.ShouldBe(100);
        stats.MaxDurationMs.ShouldBe(200);
        stats.ErrorCount.ShouldBe(0);
    }

    [Fact]
    public void Calculate_MultipleEndpoints_SortedByRequestCount()
    {
        var records = new[]
        {
            CreateMetrics("/api/users", "GET", 100),
            CreateMetrics("/api/products", "GET", 50),
            CreateMetrics("/api/products", "GET", 60),
            CreateMetrics("/api/products", "GET", 70),
            CreateMetrics("/api/orders", "POST", 200),
            CreateMetrics("/api/orders", "POST", 300),
        };

        var result = EndpointStatsCalculator.Calculate(records);

        result.Count.ShouldBe(3);
        result[0].Path.ShouldBe("/api/products"); // 3 requests
        result[1].Path.ShouldBe("/api/orders");   // 2 requests
        result[2].Path.ShouldBe("/api/users");     // 1 request
    }

    [Fact]
    public void Calculate_SamePathDifferentMethods_GroupedSeparately()
    {
        var records = new[]
        {
            CreateMetrics("/api/users", "GET", 100),
            CreateMetrics("/api/users", "POST", 200),
            CreateMetrics("/api/users", "GET", 150),
        };

        var result = EndpointStatsCalculator.Calculate(records);

        result.Count.ShouldBe(2);
        var getStats = result.First(s => s.Method == "GET");
        var postStats = result.First(s => s.Method == "POST");

        getStats.RequestCount.ShouldBe(2);
        postStats.RequestCount.ShouldBe(1);
    }

    [Fact]
    public void Calculate_WithTopN_LimitsResults()
    {
        var records = new[]
        {
            CreateMetrics("/api/a", "GET", 100),
            CreateMetrics("/api/a", "GET", 100),
            CreateMetrics("/api/a", "GET", 100),
            CreateMetrics("/api/b", "GET", 100),
            CreateMetrics("/api/b", "GET", 100),
            CreateMetrics("/api/c", "GET", 100),
        };

        var result = EndpointStatsCalculator.Calculate(records, topN: 2);

        result.Count.ShouldBe(2);
        result[0].Path.ShouldBe("/api/a");
        result[1].Path.ShouldBe("/api/b");
    }

    [Fact]
    public void Calculate_ErrorCount_TracksHttpErrors()
    {
        var records = new[]
        {
            CreateMetrics("/api/users", "GET", 100, 200),
            CreateMetrics("/api/users", "GET", 100, 404),
            CreateMetrics("/api/users", "GET", 100, 500),
            CreateMetrics("/api/users", "GET", 100, 200),
        };

        var result = EndpointStatsCalculator.Calculate(records);

        result.Count.ShouldBe(1);
        result[0].ErrorCount.ShouldBe(2); // 404 + 500
    }

    [Fact]
    public void Calculate_WithTimeWindow_FiltersOldRecords()
    {
        var now = DateTime.UtcNow;
        var records = new[]
        {
            CreateMetrics("/api/users", "GET", 100, timestamp: now.AddMinutes(-10)), // Old
            CreateMetrics("/api/users", "GET", 200, timestamp: now.AddMinutes(-1)),  // Recent
        };

        var result = EndpointStatsCalculator.Calculate(records, TimeSpan.FromMinutes(5));

        result.Count.ShouldBe(1);
        result[0].RequestCount.ShouldBe(1);
        result[0].AverageDurationMs.ShouldBe(200);
    }

    [Fact]
    public void Calculate_P95Duration_ComputedCorrectly()
    {
        // 20 requests to generate meaningful P95
        var records = Enumerable.Range(1, 20)
            .Select(i => CreateMetrics("/api/test", "GET", i * 10.0))
            .ToList();

        var result = EndpointStatsCalculator.Calculate(records);

        result.Count.ShouldBe(1);
        result[0].P95DurationMs.ShouldBeGreaterThan(result[0].AverageDurationMs);
        result[0].P95DurationMs.ShouldBeLessThanOrEqualTo(result[0].MaxDurationMs);
    }
}
