namespace Tnzi.Tests.Caching;

/// <summary>
/// <see cref="CachingModule"/> 的启动自证：配了 Redis 就不能静默退回进程内内存缓存。
/// </summary>
/// <remarks>
/// 这条失效没有任何症状 —— 配置、日志、接口返回全部正常，只是每个实例各存各的，典型表现是
/// 「验证码在 A 实例生成、B 实例校验失败」这种查不出所以然的间歇故障。与 <c>Tnzi.Storage.Cloud</c>
/// 同口径：配了却没加载**抛异常并指名要加载的包**，绝不回退。
/// </remarks>
public class CachingModuleStartupTests
{
    [Fact]
    public async Task RedisConfiguredButCacheStillInMemory_FailsStartupNamingThePackage()
    {
        using var provider = BuildProvider("Redis", cacheReplaced: false);

        var ex = await Assert.ThrowsAsync<ConfigurationException>(() =>
            new CachingModule().OnApplicationInitializationAsync(new ApplicationInitializationContext(provider)));

        Assert.Contains("Tnzi.Redis", ex.Message);
        Assert.Contains("RedisCachingModule", ex.Message);
        Assert.Contains("Caching:Type", ex.Message);
    }

    [Fact]
    public async Task RedisConfiguredAndCacheReplaced_Starts()
    {
        // 在 Redis 之上包一层（L1 + L2、带指标的装饰器）是正当做法：只对「仍是核心内存实现」报错。
        using var provider = BuildProvider("Redis", cacheReplaced: true);

        await new CachingModule().OnApplicationInitializationAsync(new ApplicationInitializationContext(provider));
    }

    [Theory]
    [InlineData("Memory")]
    [InlineData("memory")]
    public async Task MemoryConfigured_StartsWithTheInMemoryCache(string type)
    {
        using var provider = BuildProvider(type, cacheReplaced: false);

        await new CachingModule().OnApplicationInitializationAsync(new ApplicationInitializationContext(provider));
    }

    [Fact]
    public void Validator_DoesNotRequireCachingRedisConnectionString()
    {
        // 连接串有三个来源（Redis.ConnectionString > Caching.RedisConnectionString > ConnectionStrings:Redis），
        // 校验器只看得见第二个。在这里硬性要求它，会让按文档只配第一个来源的应用启动即失败；
        // 三个来源都缺由 Redis 模块在解析处报，错误消息列出全部三个键。
        var result = new CachingOptionsValidator().Validate(null, new CachingOptions { Type = "Redis" });

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    private static ServiceProvider BuildProvider(string type, bool cacheReplaced)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddOptions<CachingOptions>().Configure(o => o.Type = type);

        if (cacheReplaced)
        {
            services.AddSingleton(new Mock<ICache>().Object);
        }
        else
        {
            services.AddSingleton<ICache, MemoryCacheService>();
        }

        return services.BuildServiceProvider();
    }
}
