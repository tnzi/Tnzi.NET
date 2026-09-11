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
}
