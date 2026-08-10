namespace Tnzi.Tests.Caching;

/// <summary>
/// 计数器读写往返契约：<c>IncrementAsync</c> 写进去的数，必须读得回来。
/// </summary>
/// <remarks>
/// <para>
/// 这组测试锁的是一次实测到的静默失效。<c>IncrementAsync</c> 契约上存 <see cref="long"/>，
/// 而 <c>MemoryCacheService.GetAsync&lt;T&gt;</c> 走 <c>IMemoryCache.TryGetValue&lt;TItem&gt;</c>，
/// 判定是 <c>result is TItem</c> —— 装箱的 <c>long</c> 不满足 <c>is int</c>，
/// 于是 <c>GetAsync&lt;int&gt;</c> 读同一个键必然落空返回 0。
/// </para>
/// <para>
/// 后果不是报错而是<b>闸门永不触发</b>：2FA 验证码失败锁定、滑块验证码难度自适应、
/// SignalR 消息限流三处的写入侧都正确做了原子递增，只有读出侧类型写错，于是三道防线
/// 在默认内存缓存下同时静默失效（Redis 走 JSON 反序列化，<c>int</c> 能解析，所以配了
/// Redis 的环境测得通 —— 这也是它长期没被发现的原因）。
/// </para>
/// <para>
/// <b>为什么现有测试抓不到</b>：那三处的既有测试都 <c>Mock&lt;ICache&gt;</c>。
/// mock 掉 <c>ICache</c> 的测试在定义上不可能发现两个真实实现之间的契约漂移。
/// 本文件刻意对<b>真实</b>的 <c>MemoryCacheService</c> 断言。
/// </para>
/// </remarks>
public class CacheCounterContractTests
{
    // 经接口而非具体类型持有：GetCounterAsync 是默认接口方法，只在接口上可见。
    private readonly ICache _cache;

    public CacheCounterContractTests()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<MemoryCacheService>>();
        var cachingOptions = Microsoft.Extensions.Options.Options.Create(new CachingOptions());
        _cache = new MemoryCacheService(memoryCache, logger, cachingOptions);
    }

    [Fact]
    public async Task GetCounterAsync_ReadsBackWhatIncrementAsyncWrote()
    {
        const string key = "counter-roundtrip";

        await _cache.IncrementAsync(key, 1, TimeSpan.FromMinutes(5));
        await _cache.IncrementAsync(key, 1, TimeSpan.FromMinutes(5));
        await _cache.IncrementAsync(key, 1, TimeSpan.FromMinutes(5));

        // 读不回来的表现不是报错，而是「失败次数超限」这类判定永远为假 —— 闸门静默失效。
        Assert.Equal(3L, await _cache.GetCounterAsync(key));
    }

    [Fact]
    public async Task GetCounterAsync_ReturnsZeroForUnknownKey()
    {
        Assert.Equal(0L, await _cache.GetCounterAsync("counter-absent"));
    }

    /// <summary>
    /// 记录踩过的坑本身：<c>GetAsync&lt;int&gt;</c> 读计数是<b>读不到</b>的。
    /// </summary>
    /// <remarks>
    /// 断言这个「错误行为」不是为了保护它，而是让下一个想把 <c>GetCounterAsync</c>
    /// 改回 <c>GetAsync&lt;int&gt;</c> 的人看到这条就知道为什么不能改。
    /// 若某天 <c>GetAsync&lt;T&gt;</c> 补上了数值转换兜底，这条会红 —— 那时删掉它即可。
    /// </remarks>
    [Fact]
    public async Task GetAsyncInt_SilentlyMissesALongCounter()
    {
        const string key = "counter-type-mismatch";

        await _cache.IncrementAsync(key, 7, TimeSpan.FromMinutes(5));

        Assert.Equal(0, await _cache.GetAsync<int>(key));
        Assert.Equal(7L, await _cache.GetCounterAsync(key));
    }
}
