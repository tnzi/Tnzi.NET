using System.Diagnostics.Metrics;
using Tnzi.Services;

namespace Tnzi.Tests.Caching;

/// <summary>
/// 缓存命中 / 未命中计数器不得把缓存键带进指标标签。
/// </summary>
/// <remarks>
/// 计数器挂在 <c>Tnzi.Framework</c> 这个 Meter 上，加载 OpenTelemetry 模块后默认导出。缓存键里有个人数据
/// （按邮箱 / 手机号限流的验证码重发键、按用户分区的权限键），以键为标签 = 把这些值写进监控后端长期留存，
/// 且时间序列数随用户量无界增长，触到 SDK 基数上限后整组指标折叠进溢出桶。
/// </remarks>
public class CacheMetricTagTests
{
    private const string PersonalKey = "2FA_Resend_Timestamp:alice@example.com:1:0";

    private sealed record Measurement(string Instrument, long Value, KeyValuePair<string, object?>[] Tags);

    private static MeterListener Listen(ConcurrentBag<Measurement> sink)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Tnzi.Framework"
                    && instrument.Name is "cache_hits_total" or "cache_misses_total")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            sink.Add(new Measurement(instrument.Name, value, tags.ToArray())));
        listener.Start();
        return listener;
    }

    private static void AssertNoKeyLeaked(ConcurrentBag<Measurement> sink)
    {
        Assert.NotEmpty(sink);
        foreach (var measurement in sink)
        {
            Assert.DoesNotContain(measurement.Tags, t => t.Key == "key");
            Assert.DoesNotContain(measurement.Tags, t => (t.Value as string ?? string.Empty).Contains("alice@example.com"));
        }
    }

    [Fact]
    public void RecordCacheHitAndMiss_DoNotExportTheCacheKey()
    {
        var sink = new ConcurrentBag<Measurement>();
        using var listener = Listen(sink);
        using var monitor = new PerformanceMonitorService();

        monitor.RecordCacheHit(PersonalKey);
        monitor.RecordCacheMiss(PersonalKey);

        Assert.Contains(sink, m => m.Instrument == "cache_hits_total");
        Assert.Contains(sink, m => m.Instrument == "cache_misses_total");
        AssertNoKeyLeaked(sink);

        // 内部快照计数照常（管理端性能页读的是它）
        var snapshot = monitor.GetSnapshot();
        Assert.Equal(1, snapshot.TotalCacheHits);
        Assert.Equal(1, snapshot.TotalCacheMisses);
    }

    [Fact]
    public async Task MemoryCacheService_LookupsThroughTheRealMonitor_DoNotExportTheCacheKey()
    {
        var sink = new ConcurrentBag<Measurement>();
        using var listener = Listen(sink);
        using var monitor = new PerformanceMonitorService();
        var services = new ServiceCollection().AddSingleton<IPerformanceMonitorService>(monitor).BuildServiceProvider();
        var cache = new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<MemoryCacheService>>(),
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()),
            services);

        await cache.GetAsync<string>(PersonalKey);
        await cache.SetAsync(PersonalKey, "1");
        await cache.GetAsync<string>(PersonalKey);

        AssertNoKeyLeaked(sink);
        Assert.Equal(1, monitor.GetSnapshot().TotalCacheMisses);
        Assert.Equal(1, monitor.GetSnapshot().TotalCacheHits);
    }
}
