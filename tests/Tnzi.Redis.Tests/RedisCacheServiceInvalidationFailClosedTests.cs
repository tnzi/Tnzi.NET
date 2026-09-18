using Tnzi.Redis.Tests.Fakes;

namespace Tnzi.Redis.Tests;

/// <summary>
/// 失效类写路径（Remove 一族与 Clear）fail-closed：失败记录后抛 <see cref="CacheWriteException"/>。
/// </summary>
/// <remarks>
/// 读失败与写入失败都能自愈（一次未命中，下一次读回填）；<b>删除失败不能</b> —— 键活到 TTL、无 TTL 的活到永远，
/// 而调用方看到的是「删除成功」。权限撤销的失效处理器刻意不吞异常、等着总线重试，Redis 实现却把异常吞在最底层，
/// 那条重试从来不可能触发。
/// </remarks>
public class RedisCacheServiceInvalidationFailClosedTests
{
    private static RedisCacheService BuildServiceWithDatabase(IDatabase database, string? instanceName = null)
    {
        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database);
        return new RedisCacheService(mux.Object, NullLogger<RedisCacheService>.Instance, instanceName, cacheSyncService: null);
    }

    private static Mock<IDatabase> DatabaseThatFailsDeletes()
    {
        var db = new Mock<IDatabase>();
        var boom = new TimeoutException("Timeout performing DEL");
        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ThrowsAsync(boom);
        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>())).ThrowsAsync(boom);
        db.Setup(d => d.KeyDelete(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).Throws(boom);
        db.Setup(d => d.KeyDelete(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>())).Throws(boom);
        // 键扫描与标签成员正常返回，让失败精确落在删除动作上
        db.Setup(d => d.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue[] { "app:product:1" });
        return db;
    }

    [Fact]
    public async Task RemoveAsync_WhenRedisThrows_ThrowsCacheWriteException()
    {
        var svc = BuildServiceWithDatabase(DatabaseThatFailsDeletes().Object, instanceName: "app");

        var ex = await Assert.ThrowsAsync<CacheWriteException>(() => svc.RemoveAsync("UserFunctions:42"));

        // 报告调用方传入的逻辑键，不含实例前缀
        Assert.Equal("UserFunctions:42", ex.CacheKey);
        Assert.IsType<TimeoutException>(ex.InnerException);
    }

    [Fact]
    public void Remove_WhenRedisThrows_ThrowsCacheWriteException()
    {
        var svc = BuildServiceWithDatabase(DatabaseThatFailsDeletes().Object);

        Assert.Throws<CacheWriteException>(() => svc.Remove("k"));
    }

    [Fact]
    public async Task RemoveManyAsync_WhenRedisThrows_ThrowsCacheWriteException()
    {
        var svc = BuildServiceWithDatabase(DatabaseThatFailsDeletes().Object);

        await Assert.ThrowsAsync<CacheWriteException>(() => svc.RemoveManyAsync(new[] { "a", "b" }));
    }

    [Fact]
    public async Task RemoveByTagAsync_WhenRedisThrows_ThrowsCacheWriteException()
    {
        var svc = BuildServiceWithDatabase(DatabaseThatFailsDeletes().Object, instanceName: "app");

        var ex = await Assert.ThrowsAsync<CacheWriteException>(() => svc.RemoveByTagAsync("product"));

        Assert.Equal("product", ex.CacheKey);
    }

    [Fact]
    public async Task RemoveAsync_WhenRedisSucceeds_DoesNotThrow()
    {
        var redis = new InMemoryRedis();
        var svc = new RedisCacheService(redis.Multiplexer.Object, NullLogger<RedisCacheService>.Instance, null, cacheSyncService: null);
        await svc.SetAsync("k", 1);

        await svc.RemoveAsync("k");

        Assert.False(await svc.ExistsAsync("k"));
    }

    /// <summary>
    /// 与失效路径对照：Set 失败仍是 fail-open —— 写不进去等于一次未命中，下一次读会回填，自愈。
    /// </summary>
    [Fact]
    public async Task SetAsync_WhenRedisThrows_StaysFailOpen()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .ThrowsAsync(new TimeoutException("Timeout performing SET"));
        var svc = BuildServiceWithDatabase(db.Object);

        await svc.SetAsync("k", 1);
    }
    // ---- 基于 SCAN 的一族：RemoveByPrefix / RemoveByPattern / Clear ----
    //
    // 它们先在每个端点上 SCAN 收集键再 DEL。收集阶段若跳过断开的主节点或吞掉扫描失败，
    // 得到的是一个空集合，随后的 DEL 根本不会执行 —— 于是外层「DEL 失败抛出」永远碰不到，
    // 调用方看到「删除成功」而键一个都没动。权限撤销的批量失效走的正是 RemoveByPrefixAsync。

    private static RedisCacheService BuildService(InMemoryRedis redis, string? instanceName = "app")
        => new(redis.Multiplexer.Object, NullLogger<RedisCacheService>.Instance, instanceName, cacheSyncService: null);

    [Fact]
    public async Task RemoveByPrefixAsync_WhenPrimaryEndpointDisconnected_ThrowsCacheWriteException()
    {
        var redis = new InMemoryRedis();
        var svc = BuildService(redis);
        await svc.SetAsync("UserFunctions:42", 1);
        redis.Server.SetupGet(s => s.IsConnected).Returns(false);

        var ex = await Assert.ThrowsAsync<CacheWriteException>(() => svc.RemoveByPrefixAsync("UserFunctions:"));

        Assert.Equal("UserFunctions:", ex.CacheKey);
        Assert.True(redis.Strings.ContainsKey("app:UserFunctions:42"), "the key must survive: nothing was scanned, so nothing may be reported deleted");
    }

    [Fact]
    public async Task RemoveByPatternAsync_WhenScanThrows_ThrowsCacheWriteException()
    {
        var redis = new InMemoryRedis();
        var svc = BuildService(redis);
        var boom = new TimeoutException("Timeout performing SCAN");
        redis.Server
            .Setup(s => s.KeysAsync(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Throws(boom);

        var ex = await Assert.ThrowsAsync<CacheWriteException>(() => svc.RemoveByPatternAsync("user:*"));

        Assert.Same(boom, ex.InnerException);
    }

    [Fact]
    public async Task ClearAsync_WhenPrimaryEndpointDisconnected_ThrowsCacheWriteException()
    {
        var redis = new InMemoryRedis();
        var svc = BuildService(redis);
        await svc.SetAsync("k", 1);
        redis.Server.SetupGet(s => s.IsConnected).Returns(false);

        await Assert.ThrowsAsync<CacheWriteException>(() => svc.ClearAsync());

        Assert.True(redis.Strings.ContainsKey("app:k"));
    }

    [Fact]
    public void RemoveByPattern_Sync_WhenScanThrows_ThrowsCacheWriteException()
    {
        var redis = new InMemoryRedis();
        var svc = BuildService(redis);
        redis.Server
            .Setup(s => s.Keys(It.IsAny<int>(), It.IsAny<RedisValue>(), It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Throws(new TimeoutException("Timeout performing SCAN"));

        Assert.Throws<CacheWriteException>(() => svc.RemoveByPattern("user:*"));
    }

    [Fact]
    public void Clear_Sync_WhenPrimaryEndpointDisconnected_ThrowsCacheWriteException()
    {
        var redis = new InMemoryRedis();
        var svc = BuildService(redis);
        redis.Server.SetupGet(s => s.IsConnected).Returns(false);

        Assert.Throws<CacheWriteException>(() => svc.Clear());
    }

    /// <summary>
    /// 集群里断开的<b>副本</b>不算失败：键空间的权威副本在主节点上，副本本来就被跳过。
    /// 只有主节点缺席才意味着「有键没扫到」。
    /// </summary>
    [Fact]
    public async Task RemoveByPrefixAsync_WhenOnlyAReplicaIsDisconnected_StillDeletes()
    {
        var redis = new InMemoryRedis();
        var primaryEndpoint = new System.Net.DnsEndPoint("primary", 6379);
        var replicaEndpoint = new System.Net.DnsEndPoint("replica", 6379);
        var replica = new Mock<IServer>(MockBehavior.Loose);
        replica.SetupGet(s => s.IsConnected).Returns(false);
        replica.SetupGet(s => s.IsReplica).Returns(true);
        redis.Multiplexer.Setup(m => m.GetEndPoints(It.IsAny<bool>())).Returns([primaryEndpoint, replicaEndpoint]);
        redis.Multiplexer.Setup(m => m.GetServer(primaryEndpoint, It.IsAny<object>())).Returns(redis.Server.Object);
        redis.Multiplexer.Setup(m => m.GetServer(replicaEndpoint, It.IsAny<object>())).Returns(replica.Object);
        var svc = BuildService(redis);
        await svc.SetAsync("UserFunctions:42", 1);

        await svc.RemoveByPrefixAsync("UserFunctions:");

        Assert.False(redis.Strings.ContainsKey("app:UserFunctions:42"));
    }

    /// <summary>
    /// 带标签的写入是<b>失效路径的前半段</b>：值写进去了而标签索引没建上，之后 <c>RemoveByTagAsync</c>
    /// 找不到它，这条目活到 TTL —— 与删除失败同样不能自愈，所以 fail-closed。此前它裸抛 StackExchange 的原始异常
    /// （既不是 fail-open 也不是 <see cref="CacheWriteException"/>），而文档把它列在 fail-open 一栏。
    /// </summary>
    [Fact]
    public async Task SetWithTagsAsync_WhenTagIndexWriteFails_ThrowsCacheWriteException()
    {
        var db = new Mock<IDatabase>();
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .ReturnsAsync(true);
        db.Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new TimeoutException("Timeout performing SADD"));
        var svc = BuildServiceWithDatabase(db.Object, instanceName: "app");

        var ex = await Assert.ThrowsAsync<CacheWriteException>(() => svc.SetWithTagsAsync("product:1", 1, ["catalog"]));

        Assert.Equal("product:1", ex.CacheKey);
        Assert.IsType<TimeoutException>(ex.InnerException);
    }
}
