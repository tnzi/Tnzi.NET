using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tnzi.Caching;
using Tnzi.Modules;
using Tnzi.Redis.Tests.Fakes;
using Tnzi.Services;

namespace Tnzi.Redis.Tests;

/// <summary>
/// 共用一个 Redis 的两个应用（或预发与生产）靠 <c>Caching:RedisInstanceName</c> 隔离。
/// 缓存键一直带这个前缀；锁键与失效频道此前不带，于是「隔离」只隔离了一半。
/// </summary>
public class RedisInstanceIsolationTests
{
    private static RedisResult Ok(long value) => RedisResult.Create((RedisValue)value, ResultType.Integer);

    private static (Mock<IConnectionMultiplexer> Mux, List<string> SetKeys) MuxRecordingLockKeys()
    {
        var setKeys = new List<string>();
        var db = new Mock<IDatabase>();
        db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
            .Callback((RedisKey key, RedisValue _, TimeSpan? _, When _) => setKeys.Add(key.ToString()))
            .ReturnsAsync(true);
        db.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(Ok(1));

        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);
        return (mux, setKeys);
    }

    // ---------- 分布式锁 ----------

    [Fact]
    public async Task LockKey_CarriesTheInstanceName()
    {
        var (mux, setKeys) = MuxRecordingLockKeys();
        var sut = new RedisDistributedLock(mux.Object, new LockOptions { EnableAutoRenewal = false }, instanceName: "prod");

        await using var handle = await sut.AcquireAsync("Tnzi:Outbox:Relay");

        Assert.Equal(["prod:lock:Tnzi:Outbox:Relay"], setKeys);
        Assert.Equal("Tnzi:Outbox:Relay", handle!.Key);
    }

    [Fact]
    public async Task LockKey_WithoutAnInstanceName_KeepsTheBareForm()
    {
        var (mux, setKeys) = MuxRecordingLockKeys();
        var sut = new RedisDistributedLock(mux.Object, new LockOptions { EnableAutoRenewal = false });

        await using var handle = await sut.AcquireAsync("job");

        Assert.Equal(["lock:job"], setKeys);
        Assert.Equal("job", handle!.Key);
    }

    /// <summary>
    /// ★ 回归：两个实例名不同的应用对同名锁互不排斥，同一实例名的仍然互斥。
    /// </summary>
    [Fact]
    public async Task Locks_AreExclusivePerInstanceName()
    {
        var redis = new InMemoryRedis();
        var options = new LockOptions { EnableAutoRenewal = false };
        var staging = new RedisDistributedLock(redis.Multiplexer.Object, options, instanceName: "staging");
        var production = new RedisDistributedLock(redis.Multiplexer.Object, options, instanceName: "production");
        var otherProductionNode = new RedisDistributedLock(redis.Multiplexer.Object, options, instanceName: "production");

        var stagingHandle = await staging.AcquireAsync("Tnzi:Audit:RetentionPurge");
        var productionHandle = await production.AcquireAsync("Tnzi:Audit:RetentionPurge");
        var contended = await otherProductionNode.AcquireAsync("Tnzi:Audit:RetentionPurge");

        Assert.NotNull(stagingHandle);
        Assert.NotNull(productionHandle);
        Assert.Null(contended);
    }

    // ---------- 失效频道 ----------

    [Theory]
    [InlineData("prod", "prod:Tnzi:Cache:Invalidation")]
    [InlineData(null, "Tnzi:Cache:Invalidation")]
    [InlineData("", "Tnzi:Cache:Invalidation")]
    public void InvalidationChannel_CarriesTheInstanceName(string? instanceName, string expected)
    {
        Assert.Equal(expected, RedisCacheSyncService.ChannelNameFor(instanceName));
    }

    // ---------- 模块接线 ----------

    /// <summary>模块把 <c>Caching:RedisInstanceName</c> 交给锁：锁键带前缀。</summary>
    [Fact]
    public async Task Module_PassesTheInstanceNameToTheLock()
    {
        var (mux, setKeys) = MuxRecordingLockKeys();
        await using var provider = await BuildProviderAsync(mux.Object, publishInvalidation: false);

        await using var handle = await provider.GetRequiredService<IDistributedLock>().AcquireAsync("job");

        Assert.Equal(["app:lock:job"], setKeys);
    }

    /// <summary>模块把实例名交给失效广播：发到本实例自己的频道。</summary>
    [Fact]
    public async Task Module_PublishesInvalidationsOnTheInstanceChannel()
    {
        var channels = new List<string>();
        var subscriber = new Mock<ISubscriber>();
        subscriber
            .Setup(s => s.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Callback((RedisChannel channel, RedisValue _, CommandFlags _) => channels.Add(channel.ToString()))
            .ReturnsAsync(1);
        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetSubscriber(It.IsAny<object>())).Returns(subscriber.Object);
        await using var provider = await BuildProviderAsync(mux.Object, publishInvalidation: true);

        await provider.GetRequiredService<ICacheSyncService>().PublishCacheInvalidationAsync("k", CacheOperation.Remove);

        Assert.Equal(["app:Tnzi:Cache:Invalidation"], channels);
    }

    /// <summary>模块把运行期计数交给 Redis 缓存：Redis 部署的命中率有样本。</summary>
    [Fact]
    public async Task Module_GivesTheCacheThePerformanceMonitor()
    {
        var redis = new InMemoryRedis();
        var monitor = new Mock<IPerformanceMonitorService>();
        await using var provider = await BuildProviderAsync(redis.Multiplexer.Object, publishInvalidation: false, monitor.Object);
        var cache = provider.GetRequiredService<ICache>();

        await cache.SetAsync("present", 1);
        await cache.GetAsync<int?>("present");
        await cache.GetAsync<int?>("absent");

        monitor.Verify(m => m.RecordCacheHit("present"), Times.Once);
        monitor.Verify(m => m.RecordCacheMiss("absent"), Times.Once);
    }

    private static async Task<ServiceProvider> BuildProviderAsync(IConnectionMultiplexer multiplexer, bool publishInvalidation, IPerformanceMonitorService? monitor = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Caching:Type"] = "Redis",
            ["Caching:RedisInstanceName"] = "app",
            ["Redis:ConnectionString"] = "localhost:6379",
            ["Redis:PublishCacheInvalidation"] = publishInvalidation ? "true" : "false",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<CachingOptions>(configuration.GetSection("Caching"));
        if (monitor != null)
        {
            services.AddSingleton(monitor);
        }

        var module = new RedisCachingModule();
        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        // 真实注册会去连 Redis：换成替身，其余登记原样保留
        services.RemoveAll<IConnectionMultiplexer>();
        services.AddSingleton(multiplexer);

        return services.BuildServiceProvider();
    }
}
