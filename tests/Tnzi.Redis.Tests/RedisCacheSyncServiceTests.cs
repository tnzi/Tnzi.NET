using Tnzi.Caching;

namespace Tnzi.Redis.Tests;

/// <summary>
/// 失效广播的失败对调用方可见。
/// </summary>
public class RedisCacheSyncServiceTests
{
    private static RedisCacheSyncService Build(Mock<ISubscriber> subscriber)
    {
        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetSubscriber(It.IsAny<object>())).Returns(subscriber.Object);
        return new RedisCacheSyncService(mux.Object, NullLogger<RedisCacheSyncService>.Instance);
    }

    /// <summary>
    /// ★ 回归：订阅失败此前只记一行日志就正常返回，调用方以为订阅成功，本地 L1 从此收不到失效通知。
    /// </summary>
    [Fact]
    public async Task SubscribeFailure_ReachesTheCaller()
    {
        var subscriber = new Mock<ISubscriber>();
        subscriber
            .Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("NOPERM this user has no permissions to run the 'subscribe' command"));

        await Assert.ThrowsAsync<RedisException>(
            () => Build(subscriber).SubscribeCacheInvalidationAsync((_, _) => Task.CompletedTask));
    }

    /// <summary>
    /// 发布失败抛给调用方：<see cref="RedisCacheService"/> 的后台发布据此统一记 Warning（此前那条 catch 永远碰不到）。
    /// </summary>
    [Fact]
    public async Task PublishFailure_ReachesTheCaller()
    {
        var subscriber = new Mock<ISubscriber>();
        subscriber
            .Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("connection lost"));

        await Assert.ThrowsAsync<RedisException>(
            () => Build(subscriber).PublishCacheInvalidationAsync("k", CacheOperation.Remove));
    }
}
