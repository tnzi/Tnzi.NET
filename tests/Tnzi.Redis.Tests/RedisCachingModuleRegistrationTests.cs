using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Caching;
using Tnzi.Modules;

namespace Tnzi.Redis.Tests;

/// <summary>
/// 加载了本模块之后，容器里到底应该有什么。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的两个缺陷</b>：
/// </para>
/// <list type="number">
/// <item><c>IDistributedLock</c> 的注册被关在 <c>Caching:Type == "Redis"</c> 分支里 ——
/// 只想要分布式锁（多实例互斥、定时任务防重入）而缓存继续用内存的应用，
/// 显式加载了本模块却<b>拿不到锁</b>：那个分支一进来就 return 了。</item>
/// <item><c>ICacheSyncService</c> 被无条件注册，于是 <c>RedisCacheService</c> 在 7 条写路径上
/// 逐次 <c>Task.Run</c> 发布失效通知（<c>RemoveByPatternAsync</c> 还是逐键发），
/// 而 <c>SubscribeCacheInvalidationAsync</c> 在全仓<b>零调用方</b> —— 纯开销。</item>
/// </list>
/// <para>
/// 这些断言只看<b>服务注册</b>，不解析：解析 <c>IConnectionMultiplexer</c> 会真的去连 Redis。
/// </para>
/// </remarks>
public class RedisCachingModuleRegistrationTests
{
    [Fact]
    public async Task WithMemoryCachingButARedisConnectionString_TheDistributedLockIsStillRegistered()
    {
        var services = await ConfigureAsync(new Dictionary<string, string?>
        {
            ["Caching:Type"] = "Memory",
            ["Redis:ConnectionString"] = "localhost:6379",
        });

        services.ShouldContain(d => d.ServiceType == typeof(IDistributedLock),
            "只要锁不要 Redis 缓存是正当组合；此前这条注册被关在缓存类型分支里，根本执行不到");
    }

    /// <summary>
    /// 不做缓存也没有连接串：什么都不注册，与修复前一致。
    /// </summary>
    /// <remarks>
    /// 刻意不注册一个「解析时才炸」的锁 —— 那会把可选注入的优雅降级变成运行期异常。
    /// </remarks>
    [Fact]
    public async Task WithMemoryCachingAndNoConnectionString_NothingIsRegistered()
    {
        var services = await ConfigureAsync(new Dictionary<string, string?>
        {
            ["Caching:Type"] = "Memory",
        });

        services.ShouldNotContain(d => d.ServiceType == typeof(IDistributedLock));
        services.ShouldNotContain(d => d.ServiceType == typeof(ICache));
    }

    [Fact]
    public async Task WithRedisCaching_TheCacheIsReplaced()
    {
        var services = await ConfigureAsync(new Dictionary<string, string?>
        {
            ["Caching:Type"] = "Redis",
            ["Redis:ConnectionString"] = "localhost:6379",
        });

        services.ShouldContain(d => d.ServiceType == typeof(ICache));
        services.ShouldContain(d => d.ServiceType == typeof(IDistributedLock));
    }

    /// <summary>
    /// 失效广播默认关闭 —— 框架内没有任何订阅方，发布纯粹是开销。
    /// </summary>
    [Fact]
    public async Task CacheInvalidationPublishing_IsOffByDefault()
    {
        var services = await ConfigureAsync(new Dictionary<string, string?>
        {
            ["Caching:Type"] = "Redis",
            ["Redis:ConnectionString"] = "localhost:6379",
        });

        services.ShouldNotContain(d => d.ServiceType == typeof(ICacheSyncService));
    }

    /// <summary>
    /// 消费方自建了本地 L1 时打开它 —— 那才是这条通道存在的理由。
    /// </summary>
    [Fact]
    public async Task CacheInvalidationPublishing_CanBeTurnedOn()
    {
        var services = await ConfigureAsync(new Dictionary<string, string?>
        {
            ["Caching:Type"] = "Redis",
            ["Redis:ConnectionString"] = "localhost:6379",
            ["Redis:PublishCacheInvalidation"] = "true",
        });

        services.ShouldContain(d => d.ServiceType == typeof(ICacheSyncService));
    }

    private static async Task<IServiceCollection> ConfigureAsync(Dictionary<string, string?> settings)
    {
        var module = new RedisCachingModule();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        return services;
    }
}
