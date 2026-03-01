using Tnzi.Performance.Models;
using Tnzi.Performance.Services;

namespace Tnzi.Performance.Tests;

public class PerformanceCollectorTests
{
    private static PerformanceMetrics CreateMetrics(string path = "/api/test", string method = "GET", double durationMs = 100, int statusCode = 200, string? userId = null, string? requestId = null)
    {
        return new PerformanceMetrics(
            Path: path,
            Method: method,
            StatusCode: statusCode,
            DurationMs: durationMs,
            RequestSizeBytes: null,
            ResponseSizeBytes: null,
            UserId: userId,
            RequestId: requestId,
            Timestamp: DateTime.UtcNow);
    }

    // --- Percentile Integration Tests ---

    [Fact]
    public void GetPercentiles_WithRecords_ReturnsValidResult()
    {
        var collector = new PerformanceCollector();
        for (var i = 1; i <= 100; i++)
        {
            collector.Record(CreateMetrics(durationMs: i));
        }

        var result = collector.GetPercentiles();

        result.SampleCount.ShouldBe(100);
        result.Min.ShouldBe(1);
        result.Max.ShouldBe(100);
        result.P50.ShouldBeInRange(49, 52);
        result.P95.ShouldBeInRange(94, 96);
        result.P99.ShouldBeInRange(98, 100);
    }

    [Fact]
    public void GetPercentiles_EmptyCollector_ReturnsZeros()
    {
        var collector = new PerformanceCollector();
        var result = collector.GetPercentiles();

        result.SampleCount.ShouldBe(0);
        result.P50.ShouldBe(0);
        result.P95.ShouldBe(0);
        result.P99.ShouldBe(0);
    }

    [Fact]
    public void GetPercentiles_WithTimeWindow_FiltersCorrectly()
    {
        var collector = new PerformanceCollector();
        // Add recent records
        for (var i = 0; i < 10; i++)
        {
            collector.Record(CreateMetrics(durationMs: 50));
        }

        var result = collector.GetPercentiles(TimeSpan.FromMinutes(5));
        result.SampleCount.ShouldBe(10);
    }

    // --- Endpoint Stats Integration Tests ---

    [Fact]
    public void GetEndpointStats_MultipleEndpoints_AggregatesCorrectly()
    {
        var collector = new PerformanceCollector();
        for (var i = 0; i < 5; i++) collector.Record(CreateMetrics("/api/users", "GET", 100));
        for (var i = 0; i < 3; i++) collector.Record(CreateMetrics("/api/products", "POST", 200));

        var result = collector.GetEndpointStats();

        result.Count.ShouldBe(2);
        result[0].Path.ShouldBe("/api/users");
        result[0].RequestCount.ShouldBe(5);
        result[1].Path.ShouldBe("/api/products");
        result[1].RequestCount.ShouldBe(3);
    }

    [Fact]
    public void GetEndpointStats_WithTopN_LimitsResults()
    {
        var collector = new PerformanceCollector();
        collector.Record(CreateMetrics("/api/a", durationMs: 100));
        collector.Record(CreateMetrics("/api/a", durationMs: 100));
        collector.Record(CreateMetrics("/api/b", durationMs: 100));
        collector.Record(CreateMetrics("/api/c", durationMs: 100));

        var result = collector.GetEndpointStats(topN: 1);

        result.Count.ShouldBe(1);
        result[0].Path.ShouldBe("/api/a");
    }

    // --- Slow Request Detail Tests ---

    [Fact]
    public void GetSlowRequestDetails_WithThreshold_FiltersCorrectly()
    {
        var collector = new PerformanceCollector();
        collector.Record(CreateMetrics(durationMs: 50));
        collector.Record(CreateMetrics(durationMs: 500, userId: "user1", requestId: "req-1"));
        collector.Record(CreateMetrics(durationMs: 1000, userId: "user2", requestId: "req-2"));

        var result = collector.GetSlowRequestDetails(10, thresholdMs: 100);

        result.Count.ShouldBe(2);
        result[0].DurationMs.ShouldBe(1000); // Sorted descending
        result[0].UserId.ShouldBe("user2");
        result[0].RequestId.ShouldBe("req-2");
        result[1].DurationMs.ShouldBe(500);
    }

    [Fact]
    public void GetSlowRequestDetails_WithoutThreshold_ReturnsAllSortedByDuration()
    {
        var collector = new PerformanceCollector();
        collector.Record(CreateMetrics(durationMs: 10));
        collector.Record(CreateMetrics(durationMs: 300));
        collector.Record(CreateMetrics(durationMs: 50));

        var result = collector.GetSlowRequestDetails(10);

        result.Count.ShouldBe(3);
        result[0].DurationMs.ShouldBe(300);
        result[1].DurationMs.ShouldBe(50);
        result[2].DurationMs.ShouldBe(10);
    }

    [Fact]
    public void GetSlowRequestDetails_CountLimitsResults()
    {
        var collector = new PerformanceCollector();
        for (var i = 0; i < 10; i++)
        {
            collector.Record(CreateMetrics(durationMs: (i + 1) * 100));
        }

        var result = collector.GetSlowRequestDetails(3, thresholdMs: 0);

        result.Count.ShouldBe(3);
        result[0].DurationMs.ShouldBe(1000);
        result[1].DurationMs.ShouldBe(900);
        result[2].DurationMs.ShouldBe(800);
    }

    [Fact]
    public void GetSlowRequestDetails_IncludesAllFields()
    {
        var collector = new PerformanceCollector();
        collector.Record(new PerformanceMetrics(
            Path: "/api/orders",
            Method: "POST",
            StatusCode: 500,
            DurationMs: 5000,
            RequestSizeBytes: 1024,
            ResponseSizeBytes: 2048,
            UserId: "admin-user",
            RequestId: "trace-abc-123",
            Timestamp: DateTime.UtcNow));

        var result = collector.GetSlowRequestDetails(10);

        result.Count.ShouldBe(1);
        var record = result[0];
        record.Path.ShouldBe("/api/orders");
        record.Method.ShouldBe("POST");
        record.StatusCode.ShouldBe(500);
        record.DurationMs.ShouldBe(5000);
        record.UserId.ShouldBe("admin-user");
        record.RequestId.ShouldBe("trace-abc-123");
    }

    // --- Clear Tests ---

    [Fact]
    public void Clear_RemovesAllData()
    {
        var collector = new PerformanceCollector();
        for (var i = 0; i < 50; i++) collector.Record(CreateMetrics(durationMs: i));

        collector.Clear();

        collector.GetPercentiles().SampleCount.ShouldBe(0);
        collector.GetEndpointStats().ShouldBeEmpty();
        collector.GetSlowRequestDetails(100).ShouldBeEmpty();
    }

    // --- MaxHistorySize Tests ---

    [Fact]
    public void Record_ExceedsMaxHistorySize_EvictsOldRecords()
    {
        var collector = new PerformanceCollector(maxHistorySize: 10);
        for (var i = 0; i < 20; i++)
        {
            collector.Record(CreateMetrics(durationMs: i + 1));
        }

        var result = collector.GetPercentiles();
        result.SampleCount.ShouldBeLessThanOrEqualTo(10);
    }
}
