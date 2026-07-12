using Tnzi.Redis.Tests.Fakes;

namespace Tnzi.Redis.Tests;

/// <summary>
/// RedisCacheService 行为测试。
/// 核心不变量：所有读写方法使用统一的 String 存储表示，跨方法互通；
/// 标签索引键带实例前缀；计数器失败时抛出而非静默返回 0。
/// </summary>
public class RedisCacheServiceTests
{
    private sealed record Payload(int Id, string Name);

    private static RedisCacheService BuildService(InMemoryRedis redis, string? instanceName = null)
        => new(redis.Multiplexer.Object, NullLogger<RedisCacheService>.Instance, instanceName, cacheSyncService: null);

    private static RedisCacheService BuildServiceWithDatabase(IDatabase database, string? instanceName = null)
    {
        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database);
        return new RedisCacheService(mux.Object, NullLogger<RedisCacheService>.Instance, instanceName, cacheSyncService: null);
    }

    // ============ Task 1: 统一存储格式（跨方法互通）============

    [Fact]
    public async Task SetAsync_ThenGetManyAsync_SeesValue()
    {
        // 这是修复的核心保证：以前 SetAsync 走 IDistributedCache(hash)，GetManyAsync 走裸 IDatabase(string)，
        // 互相读不到；统一为 String 后必须可见。
        var redis = new InMemoryRedis();
        var svc = BuildService(redis);

        await svc.SetAsync("user:1", new Payload(1, "alice"));

        var many = await svc.GetManyAsync<Payload>(new[] { "user:1" });

        Assert.True(many.ContainsKey("user:1"));
        Assert.Equal(new Payload(1, "alice"), many["user:1"]);
    }

    [Fact]
    public async Task SetManyAsync_ThenGetAsync_SeesValue()
    {
        // 反向：SetManyAsync(批量 String) 写入的键，GetAsync 必须能读到。
        var redis = new InMemoryRedis();
        var svc = BuildService(redis);

        await svc.SetManyAsync(new[]
        {
            new KeyValuePair<string, Payload>("a", new Payload(1, "a")),
            new KeyValuePair<string, Payload>("b", new Payload(2, "b")),
        });

        Assert.Equal(new Payload(1, "a"), await svc.GetAsync<Payload>("a"));
        Assert.Equal(new Payload(2, "b"), await svc.GetAsync<Payload>("b"));
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTrips()
    {
        var redis = new InMemoryRedis();
        var svc = BuildService(redis);

        await svc.SetAsync("k", new Payload(7, "seven"));

        Assert.Equal(new Payload(7, "seven"), await svc.GetAsync<Payload>("k"));
    }

    [Fact]
    public async Task SetWithTagsAsync_ThenGetAsync_SeesValue()
    {
        // 带标签写入的值同样是 String，普通 GetAsync 必须能读到。
        var redis = new InMemoryRedis();
        var svc = BuildService(redis);

        await svc.SetWithTagsAsync("k", new Payload(3, "c"), new[] { "t1" });

        Assert.Equal(new Payload(3, "c"), await svc.GetAsync<Payload>("k"));
    }

    [Fact]
    public async Task SetAsync_WithInstanceName_UsesPrefixedKey()
    {
        var redis = new InMemoryRedis();
        var svc = BuildService(redis, instanceName: "app1");

        await svc.SetAsync("k", new Payload(1, "x"));

        Assert.True(redis.Strings.ContainsKey("app1:k"));
        Assert.False(redis.Strings.ContainsKey("k"));
    }

    // ============ Task 2: 标签索引键带实例前缀 ============

    [Fact]
    public async Task SetWithTagsAsync_TagIndexKey_IsInstancePrefixed()
    {
        var redis = new InMemoryRedis();
        var svc = BuildService(redis, instanceName: "app1");

        await svc.SetWithTagsAsync("k", new Payload(1, "x"), new[] { "t1" });

        // 标签索引键必须带实例前缀，避免多应用共享 Redis 时互相污染
        Assert.True(redis.Sets.ContainsKey("app1:tag:t1"));
        Assert.False(redis.Sets.ContainsKey("tag:t1"));
        // 索引成员是带前缀的完整缓存键
        Assert.Contains("app1:k", redis.Sets["app1:tag:t1"]);
    }

    [Fact]
    public async Task SetWithTagsAsync_NoInstanceName_TagKeyUnprefixed()
    {
        var redis = new InMemoryRedis();
        var svc = BuildService(redis);

        await svc.SetWithTagsAsync("k", new Payload(1, "x"), new[] { "t1" });

        Assert.True(redis.Sets.ContainsKey("tag:t1"));
    }

    [Fact]
    public async Task RemoveByTagAsync_RemovesAllTaggedEntries()
    {
        var redis = new InMemoryRedis();
        var svc = BuildService(redis, instanceName: "app1");

        await svc.SetWithTagsAsync("k1", new Payload(1, "a"), new[] { "grp" });
        await svc.SetWithTagsAsync("k2", new Payload(2, "b"), new[] { "grp" });

        await svc.RemoveByTagAsync("grp");

        Assert.Null(await svc.GetAsync<Payload>("k1"));
        Assert.Null(await svc.GetAsync<Payload>("k2"));
        // 标签索引本身也被清理
        Assert.False(redis.Sets.ContainsKey("app1:tag:grp"));
    }

    [Fact]
    public async Task RemoveByTagAsync_DoesNotTouchOtherInstancesTagKey()
    {
        // 跨实例隔离：app1 的 RemoveByTag 不应删到 app2 的标签索引。
        var redis = new InMemoryRedis();
        var app1 = BuildService(redis, instanceName: "app1");
        var app2 = BuildService(redis, instanceName: "app2");

        await app1.SetWithTagsAsync("k", new Payload(1, "a"), new[] { "shared" });
        await app2.SetWithTagsAsync("k", new Payload(2, "b"), new[] { "shared" });

        await app1.RemoveByTagAsync("shared");

        // app2 的值和标签索引不受影响
        Assert.Equal(new Payload(2, "b"), await app2.GetAsync<Payload>("k"));
        Assert.True(redis.Sets.ContainsKey("app2:tag:shared"));
    }

    // ============ Task 3: 计数器错误语义（fail-closed 抛出）============

    [Fact]
    public async Task IncrementAsync_Success_ReturnsUpdatedValue()
    {
        var redis = new InMemoryRedis();
        var svc = BuildService(redis);

        Assert.Equal(1, await svc.IncrementAsync("counter"));
        Assert.Equal(6, await svc.IncrementAsync("counter", 5));
    }

    [Fact]
    public async Task IncrementAsync_OnFailure_ThrowsInsteadOfReturningZero()
    {
        var boom = new InvalidOperationException("redis unavailable");
        var db = new Mock<IDatabase>();
        db.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(boom);
        var svc = BuildServiceWithDatabase(db.Object);

        var ex = await Assert.ThrowsAsync<CacheWriteException>(() => svc.IncrementAsync("quota:user:1"));
        Assert.Same(boom, ex.InnerException);
        Assert.Equal("quota:user:1", ex.CacheKey);
    }

    [Fact]
    public async Task DecrementAsync_OnFailure_ThrowsInsteadOfReturningZero()
    {
        var boom = new InvalidOperationException("redis unavailable");
        var db = new Mock<IDatabase>();
        db.Setup(d => d.StringDecrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(boom);
        var svc = BuildServiceWithDatabase(db.Object);

        var ex = await Assert.ThrowsAsync<CacheWriteException>(() => svc.DecrementAsync("quota:user:1"));
        Assert.Same(boom, ex.InnerException);
    }

    [Fact]
    public async Task IncrementAsync_WithInstanceName_ReportsUnprefixedKeyInException()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var svc = BuildServiceWithDatabase(db.Object, instanceName: "app1");

        var ex = await Assert.ThrowsAsync<CacheWriteException>(() => svc.IncrementAsync("c"));
        // 异常里报告调用方传入的逻辑键（不含内部实例前缀）
        Assert.Equal("c", ex.CacheKey);
    }

    // ============ 读路径 fail-open（对比计数器 fail-closed）============

    [Fact]
    public async Task GetAsync_OnFailure_ReturnsDefault_FailOpen()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));
        var svc = BuildServiceWithDatabase(db.Object);

        // 读路径失败不抛，返回默认值，避免缓存故障拖垮主流程
        Assert.Null(await svc.GetAsync<Payload>("k"));
    }

    [Fact]
    public async Task ExistsAsync_OnFailure_ReturnsFalse_FailOpen()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));
        var svc = BuildServiceWithDatabase(db.Object);

        Assert.False(await svc.ExistsAsync("k"));
    }

    // ============ TrySetAsync NX 语义 ============

    [Fact]
    public async Task TrySetAsync_WhenAbsent_SetsAndReturnsTrue()
    {
        var redis = new InMemoryRedis();
        var svc = BuildService(redis);

        Assert.True(await svc.TrySetAsync("k", new Payload(1, "a")));
        Assert.Equal(new Payload(1, "a"), await svc.GetAsync<Payload>("k"));
    }

    [Fact]
    public async Task TrySetAsync_WhenPresent_ReturnsFalseAndKeepsOriginal()
    {
        var redis = new InMemoryRedis();
        var svc = BuildService(redis);

        await svc.SetAsync("k", new Payload(1, "original"));
        var second = await svc.TrySetAsync("k", new Payload(2, "override"));

        Assert.False(second);
        Assert.Equal(new Payload(1, "original"), await svc.GetAsync<Payload>("k"));
    }

    // ============ Remove ============

    [Fact]
    public async Task RemoveAsync_DeletesValue()
    {
        var redis = new InMemoryRedis();
        var svc = BuildService(redis);

        await svc.SetAsync("k", new Payload(1, "a"));
        await svc.RemoveAsync("k");

        Assert.Null(await svc.GetAsync<Payload>("k"));
    }
}
