using Tnzi.Services;

namespace Tnzi.Tests.Caching;

/// <summary>
/// <c>ICache.GetOrAddAsync</c> 的缺失判定回归测试。
///
/// 默认实现原先用 <c>GetAsync&lt;T&gt;() is not null</c> 判断命中。对值类型 T 这个判断**恒真**
/// （未命中时 <c>GetAsync</c> 返回 <c>default(T)</c>，而 <c>0 is not null</c> 为真），
/// 于是工厂永远不被调用、缓存永远不被写入，<c>GetOrAddAsync&lt;int&gt;</c> 恒返回 0。
/// 没有任何报错。
/// </summary>
public class CacheGetOrAddTests
{
    private readonly ICache _cache;

    public CacheGetOrAddTests()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<MemoryCacheService>>();
        var cachingOptions = Microsoft.Extensions.Options.Options.Create(new CachingOptions());
        _cache = new MemoryCacheService(memoryCache, logger, cachingOptions);
    }

    [Fact]
    public async Task GetOrAddAsync_ValueType_CallsTheFactoryOnMiss()
    {
        var factoryCalls = 0;

        var value = await _cache.GetOrAddAsync("counter", () =>
        {
            factoryCalls++;
            return Task.FromResult(42);
        });

        Assert.Equal(42, value);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrAddAsync_ValueType_CachesTheFactoryResult()
    {
        var factoryCalls = 0;

        Task<int> Factory()
        {
            factoryCalls++;
            return Task.FromResult(7);
        }

        var first = await _cache.GetOrAddAsync("counter", Factory);
        var second = await _cache.GetOrAddAsync("counter", Factory);

        Assert.Equal(7, first);
        Assert.Equal(7, second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrAddAsync_ValueType_ReturnsACachedZeroWithoutCallingTheFactoryAgain()
    {
        // 0 是一个合法的缓存值，不是「没有」。存过之后就不该再调工厂
        await _cache.SetAsync("zero", 0);
        var factoryCalls = 0;

        var value = await _cache.GetOrAddAsync("zero", () =>
        {
            factoryCalls++;
            return Task.FromResult(99);
        });

        Assert.Equal(0, value);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task GetOrAddAsync_ReferenceType_CallsTheFactoryOnceThenServesTheCachedValue()
    {
        var factoryCalls = 0;

        Task<string> Factory()
        {
            factoryCalls++;
            return Task.FromResult("hello");
        }

        var first = await _cache.GetOrAddAsync("greeting", Factory);
        var second = await _cache.GetOrAddAsync("greeting", Factory);

        Assert.Equal("hello", first);
        Assert.Equal("hello", second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task TryGetAsync_ReportsMissAndHitForValueTypes()
    {
        var miss = await _cache.TryGetAsync<int>("absent");
        Assert.False(miss.Found);

        await _cache.SetAsync("present", 0);
        var hit = await _cache.TryGetAsync<int>("present");
        Assert.True(hit.Found);
        Assert.Equal(0, hit.Value);
    }

    /// <summary>
    /// 经 <c>GetOrAddAsync</c> 的一次未命中只记一次 miss、一次命中只记一次 hit。
    /// 默认接口实现在拿到锁后的复查里又计了一次，命中率被低估（真实 50% 显示约 33%）。
    /// </summary>
    [Fact]
    public async Task GetOrAddAsync_RecordsOneMissForAMiss_AndOneHitForAHit()
    {
        var monitor = new Mock<IPerformanceMonitorService>();
        var services = new ServiceCollection().AddSingleton(monitor.Object).BuildServiceProvider();
        ICache cache = new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<MemoryCacheService>>(),
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()),
            services);

        await cache.GetOrAddAsync("metered", () => Task.FromResult(1));
        await cache.GetOrAddAsync("metered", () => Task.FromResult(2));

        monitor.Verify(m => m.RecordCacheMiss("metered"), Times.Once);
        monitor.Verify(m => m.RecordCacheHit("metered"), Times.Once);
    }
}
