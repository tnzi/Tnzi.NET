using System.Collections.Concurrent;
using Tnzi.Caching;
using Tnzi.Redis.Tests.Fakes;

namespace Tnzi.Redis.Tests;

/// <summary>
/// 缓存失效广播必须覆盖<b>每一条</b>删除路径，而不是其中 7 条。
/// </summary>
/// <remarks>
/// 这条通道只对「在 Redis 之上自建了 L1 的消费方」有意义：A 实例删了，B / C 的本地副本要跟着失效。
/// 按标签批量失效一组关联缓存恰恰是 L1 最需要广播的场景，此前却是唯一不广播的那个 ——
/// Redis 里删干净了、日志干净、没有异常，只有部分实例答旧值。
/// 发布是 fire-and-forget（<c>Task.Run</c>），用例经可等待的假订阅方收消息。
/// </remarks>
public class RedisCacheServiceInvalidationBroadcastTests
{
    private sealed record Payload(int Id);

    private static (RedisCacheService Service, RecordingSync Sync, InMemoryRedis Redis) Build(string? instanceName = "app")
    {
        var redis = new InMemoryRedis();
        var sync = new RecordingSync();
        var svc = new RedisCacheService(redis.Multiplexer.Object, NullLogger<RedisCacheService>.Instance, instanceName, sync);
        return (svc, sync, redis);
    }

    [Fact]
    public async Task RemoveByTagAsync_PublishesRemove_ForEachMemberKey_WithoutInstancePrefix()
    {
        var (svc, sync, _) = Build();
        await svc.SetWithTagsAsync("product:1", new Payload(1), ["catalog"]);
        await svc.SetWithTagsAsync("product:2", new Payload(2), ["catalog"]);
        await sync.SettleAsync(2);

        await svc.RemoveByTagAsync("catalog");

        var published = await sync.WaitForAsync(2);
        Assert.Equal(
            new[] { ("product:1", CacheOperation.Remove), ("product:2", CacheOperation.Remove) },
            published.OrderBy(p => p.Key));
    }

    [Fact]
    public async Task RemoveByPrefixAsync_PublishesRemove_ForEachDeletedKey_WithoutInstancePrefix()
    {
        var (svc, sync, _) = Build();
        await svc.SetAsync("user:1", new Payload(1));
        await svc.SetAsync("user:2", new Payload(2));
        await svc.SetAsync("order:1", new Payload(3));
        await sync.SettleAsync(3);

        await svc.RemoveByPrefixAsync("user:");

        var published = await sync.WaitForAsync(2);
        Assert.Equal(
            new[] { ("user:1", CacheOperation.Remove), ("user:2", CacheOperation.Remove) },
            published.OrderBy(p => p.Key));
    }

    [Fact]
    public async Task ClearAsync_PublishesOneClear_NotOneMessagePerKey()
    {
        var (svc, sync, _) = Build();
        await svc.SetAsync("a", new Payload(1));
        await svc.SetAsync("b", new Payload(2));
        await sync.SettleAsync(2);

        await svc.ClearAsync();

        var published = await sync.WaitForAsync(1);
        Assert.Equal([("*", CacheOperation.Clear)], published);
    }

    [Fact]
    public async Task Remove_Sync_PublishesRemove()
    {
        var (svc, sync, _) = Build();
        await svc.SetAsync("k", new Payload(1));
        await sync.SettleAsync(1);

        svc.Remove("k");

        Assert.Equal([("k", CacheOperation.Remove)], await sync.WaitForAsync(1));
    }

    [Fact]
    public async Task RemoveByPattern_Sync_PublishesRemove_ForEachDeletedKey()
    {
        var (svc, sync, _) = Build();
        await svc.SetAsync("user:1", new Payload(1));
        await svc.SetAsync("order:1", new Payload(2));
        await sync.SettleAsync(2);

        svc.RemoveByPattern("user:*");

        Assert.Equal([("user:1", CacheOperation.Remove)], await sync.WaitForAsync(1));
    }

    [Fact]
    public async Task Clear_Sync_PublishesOneClear()
    {
        var (svc, sync, _) = Build();
        await svc.SetAsync("a", new Payload(1));
        await sync.SettleAsync(1);

        svc.Clear();

        Assert.Equal([("*", CacheOperation.Clear)], await sync.WaitForAsync(1));
    }

    /// <summary>
    /// 没有实例前缀时键原样发出（前缀剥离逻辑不能把不带前缀的键切坏）。
    /// </summary>
    [Fact]
    public async Task RemoveByTagAsync_WithoutInstanceName_PublishesRawKeys()
    {
        var (svc, sync, _) = Build(instanceName: null);
        await svc.SetWithTagsAsync("product:1", new Payload(1), ["catalog"]);
        await sync.SettleAsync(1);

        await svc.RemoveByTagAsync("catalog");

        Assert.Equal([("product:1", CacheOperation.Remove)], await sync.WaitForAsync(1));
    }

    private sealed class RecordingSync : ICacheSyncService
    {
        private readonly ConcurrentQueue<(string Key, CacheOperation Operation)> _published = new();

        /// <summary>等 Arrange 阶段写入触发的 Update 通知都到齐，再清空 —— 发布是异步的，直接清会与它们赛跑。</summary>
        public async Task SettleAsync(int expectedCount)
        {
            await WaitForAsync(expectedCount);
            _published.Clear();
        }

        public Task PublishCacheInvalidationAsync(string key, CacheOperation operation, CancellationToken cancellationToken = default)
        {
            _published.Enqueue((key, operation));
            return Task.CompletedTask;
        }

        public Task SubscribeCacheInvalidationAsync(Func<string, CacheOperation, Task> handler, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        /// <summary>等到至少 <paramref name="count"/> 条，再多等一小会儿确认没有多发。</summary>
        public async Task<(string Key, CacheOperation Operation)[]> WaitForAsync(int count, int timeoutMs = 5000)
        {
            for (var waited = 0; waited < timeoutMs && _published.Count < count; waited += 20)
            {
                await Task.Delay(20);
            }

            await Task.Delay(100);
            return _published.ToArray();
        }
    }
}
