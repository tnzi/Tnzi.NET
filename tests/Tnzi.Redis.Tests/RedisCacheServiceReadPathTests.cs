using Tnzi.Caching;
using Tnzi.Redis.Tests.Fakes;
using Tnzi.Services;

namespace Tnzi.Redis.Tests;

/// <summary>
/// 读路径：<c>TryGetAsync</c> 的「在不在」、读不出来的旧条目、命中率计数；以及 <c>Clear</c> 的失败关闭。
/// </summary>
public class RedisCacheServiceReadPathTests
{
    private sealed record CachedProfile(string Name, int Age);

    private static RedisCacheService Build(InMemoryRedis redis, string? instanceName = "app", IPerformanceMonitorService? monitor = null)
        => new(redis.Multiplexer.Object, NullLogger<RedisCacheService>.Instance, instanceName, cacheSyncService: null, performanceMonitor: monitor);

    /// <summary>
    /// ★ 回归：部署改了缓存 DTO 的属性类型，旧条目还没过期。默认实现答「命中了 null」，
    /// <c>GetOrAddAsync</c> 在剩余 TTL 内对每个调用都答 null、从不重建。
    /// </summary>
    [Fact]
    public async Task UnreadableEntry_IsAMiss_AndGetOrAddRebuildsIt()
    {
        var redis = new InMemoryRedis();
        redis.Strings["app:profile:1"] = "{\"Name\":\"Ada\",\"Age\":\"thirty-six\"}";
        ICache cache = Build(redis);

        var (found, _) = await cache.TryGetAsync<CachedProfile>("profile:1");
        var rebuilt = await cache.GetOrAddAsync("profile:1", () => Task.FromResult(new CachedProfile("Ada", 36)));

        Assert.False(found);
        Assert.Equal(new CachedProfile("Ada", 36), rebuilt);
        Assert.Equal(new CachedProfile("Ada", 36), await cache.GetAsync<CachedProfile>("profile:1"));
    }

    [Fact]
    public async Task CachedValue_IsFoundWithOneGetAndNoExistsRoundTrip()
    {
        var redis = new InMemoryRedis();
        ICache cache = Build(redis);
        await cache.SetAsync("counter", 0);

        var (found, value) = await cache.TryGetAsync<int>("counter");

        Assert.True(found);
        Assert.Equal(0, value);
        redis.Database.Verify(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Once);
        redis.Database.Verify(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    /// <summary>缓存了 null 是「在」：与默认实现同一回答。</summary>
    [Fact]
    public async Task CachedNull_IsFound()
    {
        var redis = new InMemoryRedis();
        ICache cache = Build(redis);
        await cache.SetAsync<string?>("nothing", null);

        var (found, value) = await cache.TryGetAsync<string>("nothing");

        Assert.True(found);
        Assert.Null(value);
    }

    [Fact]
    public async Task MissingKey_IsNotFound()
    {
        ICache cache = Build(new InMemoryRedis());

        var (found, _) = await cache.TryGetAsync<int>("absent");

        Assert.False(found);
    }

    // ---------- 命中率计数 ----------

    [Fact]
    public async Task ReadPaths_RecordHitsAndMisses()
    {
        var redis = new InMemoryRedis();
        var monitor = new Mock<IPerformanceMonitorService>();
        var cache = Build(redis, monitor: monitor.Object);
        await cache.SetAsync("a", 1);
        await cache.SetAsync("b", 2);

        await cache.GetAsync<int?>("a");
        cache.Get<int?>("zzz");
        await cache.TryGetAsync<int>("b");
        await cache.TryGetAsync<int>("yyy");
        await cache.GetManyAsync<int>(["a", "xxx"]);

        monitor.Verify(m => m.RecordCacheHit("a"), Times.Exactly(2));
        monitor.Verify(m => m.RecordCacheHit("b"), Times.Once);
        monitor.Verify(m => m.RecordCacheMiss("zzz"), Times.Once);
        monitor.Verify(m => m.RecordCacheMiss("yyy"), Times.Once);
        monitor.Verify(m => m.RecordCacheMiss("xxx"), Times.Once);
    }

    /// <summary>
    /// 经 <c>GetOrAddAsync</c> 的一次未命中只记一次 miss、一次命中只记一次 hit。
    /// 默认接口实现在拿到锁后的复查里又计了一次，命中率被低估（真实 50% 显示约 33%）。
    /// </summary>
    [Fact]
    public async Task GetOrAdd_RecordsOneMissForAMiss_AndOneHitForAHit()
    {
        var redis = new InMemoryRedis();
        var monitor = new Mock<IPerformanceMonitorService>();
        ICache cache = Build(redis, monitor: monitor.Object);

        var first = await cache.GetOrAddAsync("metered", () => Task.FromResult(1));
        var second = await cache.GetOrAddAsync("metered", () => Task.FromResult(2));

        Assert.Equal(1, first);
        Assert.Equal(1, second);
        monitor.Verify(m => m.RecordCacheMiss("metered"), Times.Once);
        monitor.Verify(m => m.RecordCacheHit("metered"), Times.Once);
    }

    /// <summary>读不出来的旧条目经 <c>GetOrAddAsync</c> 重建：同样只记一次 miss。</summary>
    [Fact]
    public async Task GetOrAdd_RebuildingAnUnreadableEntry_RecordsOneMiss()
    {
        var redis = new InMemoryRedis();
        redis.Strings["app:profile:2"] = "{\"Name\":\"Ada\",\"Age\":\"thirty-six\"}";
        var monitor = new Mock<IPerformanceMonitorService>();
        ICache cache = Build(redis, monitor: monitor.Object);

        await cache.GetOrAddAsync("profile:2", () => Task.FromResult(new CachedProfile("Ada", 36)));

        monitor.Verify(m => m.RecordCacheMiss("profile:2"), Times.Once);
        monitor.Verify(m => m.RecordCacheHit(It.IsAny<string>()), Times.Never);
    }

    // ---------- Clear ----------

    /// <summary>
    /// ★ 没有实例名时「本实例的键」就是整个库：拒绝，而且一个 DEL 都不发。
    /// </summary>
    [Fact]
    public async Task Clear_WithoutAnInstanceName_IsRefusedAndDeletesNothing()
    {
        var redis = new InMemoryRedis();
        redis.Strings["lock:Tnzi:Outbox:Relay"] = "token";
        redis.Strings["Tnzi:Session:42"] = "session";
        var cache = Build(redis, instanceName: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.ClearAsync());
        Assert.Throws<InvalidOperationException>(() => cache.Clear());

        Assert.Equal(2, redis.Strings.Count);
        redis.Database.Verify(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()), Times.Never);
        redis.Database.Verify(d => d.KeyDelete(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task Clear_WithAnInstanceName_RemovesOnlyThatInstancesKeys()
    {
        var redis = new InMemoryRedis();
        redis.Strings["app:a"] = "1";
        redis.Strings["other:a"] = "1";
        redis.Strings["lock:job"] = "token";
        var cache = Build(redis, instanceName: "app");

        await cache.ClearAsync();

        Assert.Equal(["lock:job", "other:a"], redis.Strings.Keys.Order());
    }
}
