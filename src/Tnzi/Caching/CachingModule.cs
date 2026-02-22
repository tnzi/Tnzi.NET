
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
        context.Services.AddOptions<CachingOptions>()
            .Bind(context.Configuration.GetSection("Caching"))
            .ValidateWith<CachingOptions, CachingOptionsValidator>();
        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 注册内存缓存
        services.AddMemoryCache();

        // 注册缓存键生成器
        services.AddSingleton<ICacheKeyGenerator, CacheKeyGenerator>();

        // 注册默认缓存服务（线程安全的MemoryCache）
        // 如果用户需要Redis缓存，需要引用Tnzi.Redis模块
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
}