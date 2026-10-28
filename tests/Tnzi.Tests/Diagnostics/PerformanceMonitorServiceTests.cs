using System.Diagnostics.Metrics;
using Tnzi.Services;

namespace Tnzi.Tests.Diagnostics;

/// <summary>
/// <see cref="PerformanceMonitorService"/> 的缓存计数：快照里的总数，以及导出给指标系统的那两个计数器。
/// </summary>
public class PerformanceMonitorServiceTests
{
    /// <summary>
    /// ★ 缓存键不能成为指标标签：键里带用户 id、实体 id，接上导出端后每个键都是一条独立时间序列。
    /// </summary>
    [Fact]
    public void CacheHitAndMissCounters_CarryNoPerKeyTag()
    {
        using var monitor = new PerformanceMonitorService();
        var measurements = new List<(string Instrument, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Tnzi.Framework"
                && instrument.Name is "cache_hits_total" or "cache_misses_total")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            // 只收本测试这个实例的计数器：同名 Meter 在并行测试里可能还有别的实例
            if (ReferenceEquals(instrument.Meter, MeterOf(monitor)))
            {
                measurements.Add((instrument.Name, tags.ToArray()));
            }
        });
        listener.Start();

        monitor.RecordCacheHit("user:6f1c2a3b:profile");
        monitor.RecordCacheMiss("order:42");

        Assert.Equal(["cache_hits_total", "cache_misses_total"], measurements.Select(m => m.Instrument));
        Assert.All(measurements, m => Assert.Empty(m.Tags));
    }

    [Fact]
    public void Snapshot_CountsHitsAndMisses()
    {
        using var monitor = new PerformanceMonitorService();

        monitor.RecordCacheHit("a");
        monitor.RecordCacheHit("b");
        monitor.RecordCacheMiss("c");

        var snapshot = monitor.GetSnapshot();
        Assert.Equal(2, snapshot.TotalCacheHits);
        Assert.Equal(1, snapshot.TotalCacheMisses);
    }

    /// <summary>
    /// 内存缓存的批量读也记未命中：只记命中的话，批量读多的应用命中率被系统性抬高。
    /// </summary>
    [Fact]
    public async Task MemoryCacheGetMany_RecordsHitsAndMisses()
    {
        var monitor = new Mock<IPerformanceMonitorService>();
        var services = new ServiceCollection().AddSingleton(monitor.Object).BuildServiceProvider();
        var cache = new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<MemoryCacheService>>(),
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()),
            services);
        await cache.SetAsync("present", 1);

        await cache.GetManyAsync<int>(["present", "absent"]);

        monitor.Verify(m => m.RecordCacheHit("present"), Times.Once);
        monitor.Verify(m => m.RecordCacheMiss("absent"), Times.Once);
    }

    private static Meter MeterOf(PerformanceMonitorService monitor) =>
        (Meter)typeof(PerformanceMonitorService)
            .GetField("_meter", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(monitor)!;
}
