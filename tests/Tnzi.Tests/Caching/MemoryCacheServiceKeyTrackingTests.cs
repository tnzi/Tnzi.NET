namespace Tnzi.Tests.Caching;

/// <summary>
/// <see cref="MemoryCacheService"/> 的键跟踪必须在「同一键被覆盖写」之后仍然成立：
/// 批量删除（前缀 / 模式 / 标签 / 多键）以键跟踪表为候选集，跟踪一丢，条目就活在缓存里却再也删不到。
/// </summary>
/// <remarks>
/// 缺陷形态：<c>MemoryCache.Set</c> 覆盖已有键时，旧条目以 <c>EvictionReason.Replaced</c> 异步回调，
/// 而回调无条件 <c>_trackedKeys.TryRemove(key)</c>，多半落在新条目的 <c>TryAdd</c> 之后 ——
/// 于是 <c>FunctionAuthCache.ClearAllAsync</c>（<c>RemoveByPrefixAsync("UserFunctions:")</c>）
/// 清不掉并发回填过的那把键，用户持旧权限直到过期。
/// 回调是线程池派发的，测试里用 <c>Task.Delay</c> 等它跑完再做批量删除。
/// </remarks>
public class MemoryCacheServiceKeyTrackingTests
{
    private readonly MemoryCacheService _cache;

    public MemoryCacheServiceKeyTrackingTests()
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<MemoryCacheService>>();
        var cachingOptions = Microsoft.Extensions.Options.Options.Create(new CachingOptions());
        _cache = new MemoryCacheService(memoryCache, logger, cachingOptions);
    }

    private static Task LetEvictionCallbacksRun() => Task.Delay(150);

    [Fact]
    public async Task SetAsync_SameKeyTwice_RemoveByPrefixStillRemovesIt()
    {
        await _cache.SetAsync("UserFunctions:u1", "v1");
        await _cache.SetAsync("UserFunctions:u1", "v2");
        await LetEvictionCallbacksRun();

        await _cache.RemoveByPrefixAsync("UserFunctions:");

        var (found, _) = await _cache.TryGetAsync<string>("UserFunctions:u1");
        Assert.False(found);
    }

    [Fact]
    public async Task SetAsync_SameKeyTwice_RemoveByPatternStillRemovesIt()
    {
        await _cache.SetAsync("perm:u1:functions", "v1");
        await _cache.SetAsync("perm:u1:functions", "v2");
        await LetEvictionCallbacksRun();

        await _cache.RemoveByPatternAsync("perm:*:functions");

        Assert.False(await _cache.ExistsAsync("perm:u1:functions"));
    }

    [Fact]
    public async Task IncrementAsync_ThenRemoveByPattern_Removes()
    {
        // 每次递增都是一次替换写。
        await _cache.IncrementAsync("counter:a");
        await _cache.IncrementAsync("counter:a");
        await _cache.IncrementAsync("counter:a");
        await LetEvictionCallbacksRun();

        await _cache.RemoveByPatternAsync("counter:*");

        Assert.False(await _cache.ExistsAsync("counter:a"));
    }

    [Fact]
    public async Task SetWithTagsAsync_Twice_RemoveByTag_Removes()
    {
        await _cache.SetWithTagsAsync("item:1", "v1", ["tag-a"]);
        await _cache.SetWithTagsAsync("item:1", "v2", ["tag-a"]);
        await LetEvictionCallbacksRun();

        await _cache.RemoveByTagAsync("tag-a");

        Assert.False(await _cache.ExistsAsync("item:1"));
    }

    [Fact]
    public async Task TrySetAsync_AfterOverwrite_RemoveByPrefix_Removes()
    {
        await _cache.SetAsync("lock:x", "v1");
        await _cache.SetAsync("lock:x", "v2");
        await LetEvictionCallbacksRun();

        await _cache.RemoveByPrefixAsync("lock:");

        Assert.False(await _cache.ExistsAsync("lock:x"));
    }

    [Fact]
    public async Task SetManyAsync_OverwritingExisting_RemoveByPrefix_Removes()
    {
        await _cache.SetAsync("batch:1", "v1");
        await _cache.SetManyAsync(new Dictionary<string, string> { ["batch:1"] = "v2", ["batch:2"] = "v2" });
        await LetEvictionCallbacksRun();

        await _cache.RemoveByPrefixAsync("batch:");

        Assert.False(await _cache.ExistsAsync("batch:1"));
        Assert.False(await _cache.ExistsAsync("batch:2"));
    }

    [Fact]
    public async Task RemoveManyAsync_RemovesEvenWhenOverwritten()
    {
        await _cache.SetAsync("many:1", "v1");
        await _cache.SetAsync("many:1", "v2");
        await LetEvictionCallbacksRun();

        await _cache.RemoveManyAsync(["many:1"]);

        Assert.False(await _cache.ExistsAsync("many:1"));
    }

    /// <summary>
    /// 删除与同键写入交错（删除落在「登记跟踪」与「条目真正写进缓存」之间）：新条目必须仍可被批量删除看见。
    /// </summary>
    /// <remarks>
    /// 跟踪登记在 <c>Set</c> 之前（两个并发写入才能按登记顺序正确接管跟踪），于是「有令牌」不等于「已在缓存里」。
    /// 删除若拿着这枚令牌手工摘掉登记，就摘掉了一个还没落地的新条目 —— 它随后落地、活着、却再也不被
    /// RemoveByPrefix 看见。撤销登记的唯一合法出处是被驱逐条目自己的回调。
    /// 用一个在 <c>CreateEntry</c> 上插入钩子的 IMemoryCache 把交错做成确定性的。
    /// </remarks>
    [Fact]
    public async Task RemoveAsync_InterleavedBetweenTrackingAndSet_LeavesTheNewEntryRemovable()
    {
        MemoryCacheService? service = null;
        var interleaved = false;
        var hooked = new CreateEntryHookedMemoryCache(new MemoryCache(new MemoryCacheOptions()), key =>
        {
            if (interleaved)
                return;

            interleaved = true;
            service!.RemoveAsync((string)key).GetAwaiter().GetResult();
        });
        service = new MemoryCacheService(hooked, Mock.Of<ILogger<MemoryCacheService>>(), Microsoft.Extensions.Options.Options.Create(new CachingOptions()));

        await service.SetAsync("UserFunctions:u1", "v1");
        Assert.True(interleaved, "the hook must have interleaved a removal, otherwise this test proves nothing");
        Assert.True(await service.ExistsAsync("UserFunctions:u1"), "the removal ran before the entry landed, so the entry is alive");
        await LetEvictionCallbacksRun();

        await service.RemoveByPrefixAsync("UserFunctions:");

        Assert.False(await service.ExistsAsync("UserFunctions:u1"));
    }

    /// <summary>把每次 <c>CreateEntry</c>（即 <c>Set</c> 落地之前的那一刻）交给钩子的 IMemoryCache 装饰器。</summary>
    private sealed class CreateEntryHookedMemoryCache(IMemoryCache inner, Action<object> beforeCreateEntry) : IMemoryCache
    {
        public ICacheEntry CreateEntry(object key)
        {
            beforeCreateEntry(key);
            return inner.CreateEntry(key);
        }

        public void Remove(object key) => inner.Remove(key);

        public bool TryGetValue(object key, out object? value) => inner.TryGetValue(key, out value);

        public void Dispose() => inner.Dispose();
    }

    [Fact]
    public async Task ExpiredEntry_ThenRewrite_RemoveByPrefix_Removes()
    {
        // 上一版条目已过期但还没被扫描摘掉时再写同键：旧条目的回调带的是 Expired 而不是 Replaced，
        // 单看 EvictionReason 挡不住，跟踪必须按「条目身份」而不是按原因判断。
        await _cache.SetAsync("exp:1", "v1", TimeSpan.FromMilliseconds(20));
        await Task.Delay(60);
        await _cache.SetAsync("exp:1", "v2");
        await LetEvictionCallbacksRun();

        await _cache.RemoveByPrefixAsync("exp:");

        Assert.False(await _cache.ExistsAsync("exp:1"));
    }
}
