using Tnzi.Caching;
using Tnzi.Redis.Tests.Fakes;

namespace Tnzi.Redis.Tests;

/// <summary>
/// <see cref="ICache"/> 写入方法不给过期时间（<c>expiration: null</c>）时，Redis 实现与内存实现一样取
/// <c>Caching:DefaultExpirationMinutes</c>。此前 Redis 上这类键不带 TTL：<c>TrySetAsync(lockKey, "1")</c>
/// 在内存缓存上到点自动释放，上了 Redis 后持锁进程崩溃留下的键永不过期。
/// </summary>
public class RedisCacheServiceDefaultExpirationTests
{
    private static readonly TimeSpan ConfiguredDefault = TimeSpan.FromMinutes(7);

    private static RedisCacheService BuildService(InMemoryRedis redis, TimeSpan? defaultExpiration = null)
        => new(redis.Multiplexer.Object, NullLogger<RedisCacheService>.Instance, instanceName: null, cacheSyncService: null, defaultExpiration);

    [Fact]
    public async Task SetAsync_WithoutExpiration_UsesTheConfiguredDefault()
    {
        var redis = new InMemoryRedis();

        await BuildService(redis, ConfiguredDefault).SetAsync("k", "v");

        Assert.Equal(ConfiguredDefault, redis.StringTtls["k"]);
    }

    [Fact]
    public async Task TrySetAsync_WithoutExpiration_UsesTheConfiguredDefault()
    {
        var redis = new InMemoryRedis();

        Assert.True(await BuildService(redis, ConfiguredDefault).TrySetAsync("lock", "1"));

        Assert.Equal(ConfiguredDefault, redis.StringTtls["lock"]);
    }

    [Fact]
    public async Task SetManyAsync_WithoutExpiration_UsesTheConfiguredDefault()
    {
        var redis = new InMemoryRedis();

        await BuildService(redis, ConfiguredDefault).SetManyAsync(new[]
        {
            new KeyValuePair<string, string>("a", "1"),
            new KeyValuePair<string, string>("b", "2"),
        });

        Assert.Equal(ConfiguredDefault, redis.StringTtls["a"]);
        Assert.Equal(ConfiguredDefault, redis.StringTtls["b"]);
    }

    [Fact]
    public async Task SetWithTagsAsync_WithoutExpiration_UsesTheConfiguredDefault()
    {
        var redis = new InMemoryRedis();

        await BuildService(redis, ConfiguredDefault).SetWithTagsAsync("k", "v", ["tag"]);

        Assert.Equal(ConfiguredDefault, redis.StringTtls["k"]);
    }

    [Fact]
    public async Task WithoutAConfiguredDefault_TheCachingOptionsDefaultApplies()
    {
        var redis = new InMemoryRedis();

        await BuildService(redis).SetAsync("k", "v");

        Assert.Equal(TimeSpan.FromMinutes(new CachingOptions().DefaultExpirationMinutes), redis.StringTtls["k"]);
    }

    [Fact]
    public async Task ExplicitExpiration_IsKept_AndANonPositiveOneMeansNoExpiry()
    {
        var redis = new InMemoryRedis();
        var svc = BuildService(redis, ConfiguredDefault);

        await svc.SetAsync("explicit", "v", TimeSpan.FromSeconds(42));
        await svc.SetAsync("forever", "v", TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(42), redis.StringTtls["explicit"]);
        Assert.Null(redis.StringTtls["forever"]);
    }
}
