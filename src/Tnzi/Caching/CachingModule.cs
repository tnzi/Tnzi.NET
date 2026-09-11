
namespace Tnzi.Caching;

/// <summary>
/// 缓存模块
/// 配置路径：Caching
/// </summary>
public class CachingModule : TnziInfrastructureModule
{
    /// <summary>
    /// 缓存模块最先加载
    /// </summary>
    public override int LoadOrder => 0;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用启动时验证
        context.Services.AddTnziOptions<CachingOptions, CachingOptionsValidator>(context.Configuration);
        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 注册内存缓存（支持可选的条目数上限）
        var cachingOptions = context.Configuration.GetSection("Caching").Get<CachingOptions>();
        services.AddMemoryCache(options =>
        {
            if (cachingOptions?.MemorySizeLimit is > 0)
                options.SizeLimit = cachingOptions.MemorySizeLimit;
        });

        // 注册缓存键生成器
        services.AddSingleton<ICacheKeyGenerator, CacheKeyGenerator>();

        // 注册默认缓存服务（线程安全的MemoryCache）。
        // Redis 由 Tnzi.Redis 的 RedisCachingModule 用 RemoveAll + Add 接管；配了 Type=Redis 却没人接管
        // 由下面的 OnApplicationInitializationAsync 在启动期拦下，不会静默留在内存缓存上。
        services.AddSingleton<ICache, MemoryCacheService>();

        // 注册缓存失效服务
        services.AddScoped<ICacheInvalidationService>(sp =>
        {
            var cache = sp.GetRequiredService<ICache>();
            var logger = sp.GetService<ILogger<CacheInvalidationService>>();
            return new CacheInvalidationService(cache, logger);
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// 启动自证：<c>Caching:Type=Redis</c> 时，<see cref="ICache"/> 必须已被别的模块接管。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本模块无条件注册 <see cref="MemoryCacheService"/> 作为默认实现，Redis 模块加载后用 <c>RemoveAll + Add</c>
    /// 替换它。配了 <c>Type=Redis</c> 却漏了 <c>[DependsOn(typeof(RedisCachingModule))]</c> 的应用此前会
    /// <b>静默退回进程内内存缓存</b>：配置、日志、接口返回全部正常，只是每个实例各存各的，症状是
    /// 「验证码在 A 实例生成、B 实例校验失败」这种查不出所以然的间歇故障。
    /// </para>
    /// <para>
    /// 与 <c>Tnzi.Storage.Cloud</c> 同口径：配了却没加载<b>抛异常并指名要加载的包</b>，绝不回退。
    /// 只对「仍是核心内存实现」报错，不对自定义实现报错：在 Redis 之上包一层（L1 + L2 混合缓存、
    /// 带指标的装饰器）是正当做法。同一条检查也覆盖「Redis 模块已加载、但别的模块之后又注册了
    /// <c>ICache</c> 并赢了」—— 那时 Redis 模块自己的自证只能记 Error，这里把它变成启动失败。
    /// </para>
    /// </remarks>
    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var options = context.ServiceProvider.GetService<IOptions<CachingOptions>>()?.Value;
        if (!string.Equals(options?.Type, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        if (context.ServiceProvider.GetService<ICache>() is MemoryCacheService)
        {
            throw new ConfigurationException(
                "Caching:Type",
                "Caching:Type is 'Redis' but ICache still resolves to the in-process MemoryCacheService, so every " +
                "instance would keep its own cache while configuration, logs and responses all look healthy. " +
                "Load the Tnzi.Redis package ([DependsOn(typeof(RedisCachingModule))]) or set Caching:Type to " +
                "'Memory'. If the Redis module is loaded, another module re-registered ICache after it.");
        }

        return Task.CompletedTask;
    }
}
