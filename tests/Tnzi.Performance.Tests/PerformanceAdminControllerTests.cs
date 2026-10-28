using Microsoft.Extensions.Options;
using Moq;
using Tnzi.Performance.Controllers;
using Tnzi.Performance.Models;
using Tnzi.Performance.Options;
using Tnzi.Performance.Services;
using Tnzi.Services;

namespace Tnzi.Performance.Tests;

/// <summary>
/// 慢请求端点的缺省阈值：不传 <c>thresholdMs</c> 时用配置的 <c>Performance:SlowRequestThresholdMs</c>。
/// </summary>
public class PerformanceAdminControllerTests
{
    private sealed class MutableOptionsMonitor(PerformanceOptions value) : IOptionsMonitor<PerformanceOptions>
    {
        public PerformanceOptions CurrentValue { get; set; } = value;
        public PerformanceOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PerformanceOptions, string?> listener) => null;
    }

    private static PerformanceMetrics Sample(double durationMs) =>
        new("/api/orders", "GET", 200, durationMs, null, null, null, null, DateTime.UtcNow, "/api/orders");

    private static (DefaultPerformanceAdminController Controller, MutableOptionsMonitor Options) Build(double? thresholdMs, params double[] durations)
    {
        var collector = new PerformanceCollector();
        foreach (var duration in durations)
        {
            collector.Record(Sample(duration));
        }

        var options = new MutableOptionsMonitor(new PerformanceOptions { SlowRequestThresholdMs = thresholdMs });
        return (new DefaultPerformanceAdminController(collector, new Mock<IPerformanceMonitorService>().Object, options), options);
    }

    /// <summary>
    /// ★ 回归：此前缺省阈值是 0，管理页「Recent Slow Requests」把几毫秒的请求也列成慢请求，调设置中心的阈值对它毫无影响。
    /// </summary>
    [Fact]
    public void WithoutAThreshold_TheConfiguredOneApplies()
    {
        var (controller, _) = Build(1000, 5, 30, 1500);

        controller.GetSlowRequests(50).Data!.Select(r => r.DurationMs).ShouldBe([1500]);
    }

    [Fact]
    public void AnExplicitThreshold_Wins()
    {
        var (controller, _) = Build(1000, 5, 30, 1500);

        controller.GetSlowRequests(50, thresholdMs: 10).Data!.Select(r => r.DurationMs).ShouldBe([1500, 30]);
    }

    [Fact]
    public void AChangedSetting_AppliesAtOnce()
    {
        var (controller, options) = Build(1000, 5, 30, 1500);

        options.CurrentValue = new PerformanceOptions { SlowRequestThresholdMs = 20 };

        controller.GetSlowRequests(50).Data!.Select(r => r.DurationMs).ShouldBe([1500, 30]);
    }

    /// <summary>没有配置阈值：返回最慢的请求。</summary>
    [Fact]
    public void WithoutAnyThreshold_TheSlowestRequestsAreReturned()
    {
        var (controller, _) = Build(null, 5, 30, 1500);

        controller.GetSlowRequests(2).Data!.Select(r => r.DurationMs).ShouldBe([1500, 30]);
    }
}
