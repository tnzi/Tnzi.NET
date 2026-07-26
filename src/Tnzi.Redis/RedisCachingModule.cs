
namespace Tnzi.Redis;

/// <summary>
/// Redis 缓存模块
/// 配置路径：Redis（可选），主要使用 Caching 配置
/// </summary>
[DependsOn(typeof(CachingModule))]
public class RedisCachingModule : TnziInfrastructureModule
{
    /// <summary>
    /// Redis 模块在 Caching 模块之后加载
    /// </summary>
    public override int LoadOrder => 10;

    /// <summary>
    /// 预配置服务
    /// </summary>
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册 Redis 配置选项并启用启动时验证
        context.Services.AddTnziOptions<RedisOptions, RedisOptionsValidator>(context.Configuration);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 配置服务
    /// </summary>
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var configuration = context.Configuration;

        // 在 ConfigureServicesAsync 阶段，IOptions 可能还未构建完成
        // 使用临时方式检查配置类型，但实际配置获取在工厂函数中进行
        var tempCachingOptions = configuration.GetSection("Caching").Get<CachingOptions>();

        // 检查是否应该使用 Redis
        // 如果 Caching.Type 不是 "Redis"，则不注册 Redis 服务
        if (tempCachingOptions != null &&
            !string.Equals(tempCachingOptions.Type, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            // 如果配置类型不是 Redis，不注册 Redis 服务
            return Task.CompletedTask;
        }

        // 提取连接字符串获取逻辑（避免重复代码），并展开 ${VAR} 占位符
        string GetConnectionString(IConfiguration config, CachingOptions? cachingOpts, RedisOptions redisOpts)
        {
            var raw = redisOpts.ConnectionString
                ?? cachingOpts?.RedisConnectionString
                ?? config.GetConnectionString("Redis")
                ?? throw new InvalidOperationException(
                    "Redis connection string is required when using Redis caching. " +
                    "Configure Redis.ConnectionString, Caching.RedisConnectionString, " +
                    "or ConnectionStrings:Redis in your configuration file.");
            return ConnectionStringExpander.Expand(raw, config);
        }

        // 注册 Redis 连接多路复用器
        // 注意：AddStackExchangeRedisCache 内部也会创建 ConnectionMultiplexer，
        // 但我们显式注册一个以便其他服务（如 IDistributedLock）复用同一个连接
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var cachingOptions = provider.GetService<IOptions<CachingOptions>>()?.Value;
            var redisOptions = provider.GetService<IOptions<RedisOptions>>()?.Value ?? new RedisOptions();
            var config = provider.GetService<IConfiguration>()
                ?? throw new InvalidOperationException("IConfiguration service is not available.");

            var connectionString = GetConnectionString(config, cachingOptions, redisOptions);
            var configOptions = ConfigurationOptions.Parse(connectionString);
            var connOptions = redisOptions.Connection;

            // 应用连接选项配置
            configOptions.AbortOnConnectFail = connOptions.AbortOnConnectFail;
            configOptions.ConnectRetry = connOptions.ConnectRetry;
            configOptions.ConnectTimeout = connOptions.ConnectTimeout;
            configOptions.SyncTimeout = connOptions.SyncTimeout;

            try
            {
                var multiplexer = ConnectionMultiplexer.Connect(configOptions);
                var logger = provider.GetService<ILogger<RedisCachingModule>>();
                logger?.LogInformation("Successfully connected to Redis at {Endpoint}", SanitizeConnectionString(connectionString));
                return multiplexer;
            }
            catch (Exception ex)
            {
                var logger = provider.GetService<ILogger<RedisCachingModule>>();
                logger?.LogError(ex, "Failed to connect to Redis at {Endpoint}", SanitizeConnectionString(connectionString));
                throw;
            }
        });

        // 注册分布式缓存（Microsoft.Extensions.Caching.StackExchangeRedis）
        // 使用 ConnectionMultiplexerFactory 回调共享已注册的 IConnectionMultiplexer，
        // 避免 AddStackExchangeRedisCache 内部再创建第二个连接
        services.AddStackExchangeRedisCache(options =>
        {
            var cachingOptions = configuration.GetSection("Caching").Get<CachingOptions>();
            options.InstanceName = cachingOptions?.RedisInstanceName;
        });

        // 通过 IPostConfigureOptions 注入 ConnectionMultiplexerFactory，使用已注册的 IConnectionMultiplexer
        services.AddSingleton<IPostConfigureOptions<RedisCacheOptions>>(provider =>
        {
            var multiplexer = provider.GetRequiredService<IConnectionMultiplexer>();
            return new PostConfigureRedisCache(multiplexer);
        });

        // 注册 Redis 缓存同步服务
        services.AddSingleton<ICacheSyncService>(provider =>
        {
            var connectionMultiplexer = provider.GetRequiredService<IConnectionMultiplexer>();
            var logger = provider.GetRequiredService<ILogger<RedisCacheSyncService>>();
            return new RedisCacheSyncService(connectionMultiplexer, logger);
        });

        // 注册 Redis 缓存服务（替换默认的内存缓存实现）
        // 使用 RemoveAll 确保替换 CachingModule 注册的默认实现
        // 注意：RedisCacheService 直接使用裸 IDatabase（String 表示），不再依赖 IDistributedCache。
        // IDistributedCache 仍通过 AddStackExchangeRedisCache 注册，供会话/数据保护等其它消费者使用。
        services.RemoveAll<ICache>();
        services.AddSingleton<ICache>(provider =>
        {
            var connectionMultiplexer = provider.GetRequiredService<IConnectionMultiplexer>();
            var logger = provider.GetRequiredService<ILogger<RedisCacheService>>();
            var cacheSyncService = provider.GetService<ICacheSyncService>();

            // 从已验证的配置获取实例名称
            var cachingOptions = provider.GetService<IOptions<CachingOptions>>()?.Value;
            var instanceName = cachingOptions?.RedisInstanceName;

            return new RedisCacheService(connectionMultiplexer, logger, instanceName, cacheSyncService);
        });

        // 注册分布式锁
        services.AddSingleton<IDistributedLock>(provider =>
        {
            var connectionMultiplexer = provider.GetRequiredService<IConnectionMultiplexer>();
            var lockOptions = provider.GetService<IOptions<RedisOptions>>()?.Value?.Lock;
            var loggerFactory = provider.GetService<ILoggerFactory>();
            return new RedisDistributedLock(connectionMultiplexer, lockOptions, loggerFactory);
        });

        return Task.CompletedTask;
    }

    public override Task OnApplicationShutdownAsync(ApplicationShutdownContext context)
    {
        // 关闭 Redis 连接
        // 注意：IConnectionMultiplexer 作为 Singleton 注册，DI 容器会在应用关闭时自动释放
        // 这里显式释放以确保连接被正确关闭
        try
        {
            var connectionMultiplexer = context.ServiceProvider.GetService<IConnectionMultiplexer>();
            if (connectionMultiplexer != null && connectionMultiplexer.IsConnected)
            {
                // 仅调用 Close() 优雅断开连接
                // 不调用 Dispose()，因为 IConnectionMultiplexer 作为 Singleton 注册，
                // DI 容器关闭时会自动调用 Dispose()，避免 double-dispose
                connectionMultiplexer.Close();
            }
        }
        catch (Exception ex)
        {
            // 记录异常但不抛出，因为应用正在关闭
            var logger = context.ServiceProvider.GetService<ILogger<RedisCachingModule>>();
            logger?.LogWarning(ex, "Error occurred while closing Redis connection during application shutdown.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 从连接字符串中提取 host:port，移除密码等敏感信息，用于日志记录
    /// </summary>
    private static string SanitizeConnectionString(string connectionString)
    {
        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            var endpoints = options.EndPoints;
            if (endpoints.Count == 0)
                return "(unknown)";

            return string.Join(",", endpoints.Select(ep => ep.ToString()));
        }
        catch
        {
            return "(invalid connection string)";
        }
    }
}

/// <summary>
/// 通过 IPostConfigureOptions 将已注册的 IConnectionMultiplexer 注入 RedisCacheOptions，
/// 避免 AddStackExchangeRedisCache 内部创建第二个连接
/// </summary>
internal class PostConfigureRedisCache : IPostConfigureOptions<RedisCacheOptions>
{
    private readonly IConnectionMultiplexer _multiplexer;

    public PostConfigureRedisCache(IConnectionMultiplexer multiplexer)
    {
        _multiplexer = Check.NotNull(multiplexer);
    }

    public void PostConfigure(string? name, RedisCacheOptions options)
    {
        options.ConnectionMultiplexerFactory = () => Task.FromResult(_multiplexer);
    }
}