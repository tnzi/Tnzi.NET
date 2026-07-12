
namespace Tnzi.HealthChecks;

/// <summary>
/// 健康检查模块
/// 配置路径：HealthChecks
///
/// 支持的健康检查：
/// - Cache: 内存缓存检查（默认启用）
/// - Database: 数据库连接检查（需显式启用）
/// - Redis: Redis 分布式缓存检查（需显式启用）
/// - EventBus: 事件总线检查（需显式启用）
///
/// 端点：
/// - {Path} (/health) - 完整健康检查
/// - {LivenessPath} (/health/live) - Kubernetes 存活探针（仅检查进程存活）
/// - {ReadinessPath} (/health/ready) - Kubernetes 就绪探针（检查所有依赖）
/// </summary>
[DependsOn(typeof(CachingModule))]
public class HealthChecksModule : TnziFrameworkModule
{
    public override int LoadOrder => 50;

    /// <summary>
    /// 缓存 JSON 序列化选项，避免每次请求重复创建
    /// </summary>
    private static readonly JsonSerializerOptions DetailedResponseJsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 详细输出响应缓存
    /// </summary>
    private static readonly HealthCheckResponseCache ResponseCache = new();

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用启动时验证
        context.Services.AddTnziOptions<HealthChecksOptions, HealthChecksOptionsValidator>(context.Configuration);

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, HealthChecksPermissions>();

        var options = context.Configuration
            .GetSection("HealthChecks")
            .Get<HealthChecksOptions>() ?? new HealthChecksOptions();

        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        // 初始化 HealthChecks builder（具体的检查项在 PostConfigure 中添加，确保能发现所有已注册的服务）
        context.Services.AddHealthChecks();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 在所有模块的 ConfigureServices 完成后注册健康检查项
    /// 确保能发现所有已注册的 DbContext、IDistributedCache、IEventBus 等服务
    /// </summary>
    public override Task PostConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var options = context.Configuration
            .GetSection("HealthChecks")
            .Get<HealthChecksOptions>() ?? new HealthChecksOptions();

        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        var healthChecksBuilder = context.Services.AddHealthChecks();
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        // 添加缓存健康检查（内存缓存）
        if (options.EnableCacheCheck)
        {
            healthChecksBuilder.AddCheck<Checks.CacheHealthCheck>(
                "cache",
                HealthStatus.Degraded,
                ["cache", "infrastructure"],
                timeout);
        }

        // 添加数据库健康检查（支持多 DbContext）
        if (options.EnableDatabaseCheck)
        {
            var dbContextTypes = new Checks.RegisteredDbContextTypes();
            foreach (var descriptor in context.Services)
            {
                var serviceType = descriptor.ServiceType;
                if (serviceType != typeof(DbContext)
                    && typeof(DbContext).IsAssignableFrom(serviceType)
                    && serviceType.IsClass
                    && !serviceType.IsAbstract
                    && !dbContextTypes.Types.Contains(serviceType))
                {
                    dbContextTypes.Types.Add(serviceType);
                }
            }

            if (dbContextTypes.Types.Count > 0)
            {
                context.Services.AddSingleton(dbContextTypes);
                healthChecksBuilder.AddCheck<Checks.DatabaseHealthCheck>(
                    "database",
                    HealthStatus.Unhealthy,
                    ["database", "infrastructure"],
                    timeout);
            }
        }

        // 添加 Redis 健康检查
        if (options.EnableRedisCheck)
        {
            var hasDistributedCache = context.Services.Any(s =>
                s.ServiceType == typeof(IDistributedCache));

            if (hasDistributedCache)
            {
                healthChecksBuilder.AddCheck<Checks.RedisHealthCheck>(
                    "redis",
                    HealthStatus.Unhealthy,
                    ["redis", "cache", "infrastructure"],
                    timeout);
            }
        }

        // 添加事件总线健康检查
        if (options.EnableEventBusCheck)
        {
            var hasEventBus = context.Services.Any(s =>
                s.ServiceType == typeof(IEventBus));

            if (hasEventBus)
            {
                healthChecksBuilder.AddCheck<Checks.EventBusHealthCheck>(
                    "eventbus",
                    HealthStatus.Degraded,
                    ["eventbus", "messaging", "infrastructure"],
                    timeout);
            }
        }

        return Task.CompletedTask;
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var webApp = context.WebApp;
        if (webApp == null)
        {
            return Task.CompletedTask;
        }

        var options = context.ServiceProvider
            .GetRequiredService<IOptions<HealthChecksOptions>>()
            .Value;

        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        // 初始化响应缓存时长
        ResponseCache.SetDuration(options.CacheDurationSeconds);

        // 完整健康检查端点
        var healthCheckOptions = new HealthCheckOptions();
        if (options.DetailedOutput)
        {
            healthCheckOptions.ResponseWriter = WriteDetailedResponseAsync;
        }
        webApp.MapHealthChecks(options.Path, healthCheckOptions);

        // Liveness probe - 仅检查进程存活
        webApp.MapHealthChecks(options.LivenessPath, new HealthCheckOptions
        {
            Predicate = _ => false // 不执行任何健康检查，仅验证应用响应
        });

        // Readiness probe - 检查所有依赖
        var readinessOptions = new HealthCheckOptions();
        if (options.DetailedOutput)
        {
            readinessOptions.ResponseWriter = WriteDetailedResponseAsync;
        }
        webApp.MapHealthChecks(options.ReadinessPath, readinessOptions);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 输出详细的健康检查结果（JSON 格式），支持响应缓存
    /// </summary>
    private static async Task WriteDetailedResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        // 尝试使用缓存响应（包含状态码，确保缓存窗口内状态一致）
        var cached = ResponseCache.TryGetCachedResponse();
        if (cached != null)
        {
            context.Response.StatusCode = cached.Value.StatusCode;
            await context.Response.WriteAsync(cached.Value.Response);
            return;
        }

        var result = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            entries = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                description = e.Value.Description,
                data = e.Value.Data.Count > 0 ? e.Value.Data : null,
                exception = e.Value.Exception?.Message
            })
        };

        var response = JsonSerializer.Serialize(result, DetailedResponseJsonOptions);

        // 更新缓存（含当前状态码）
        ResponseCache.UpdateCache(response, context.Response.StatusCode);

        await context.Response.WriteAsync(response);
    }

    /// <summary>
    /// 健康检查响应缓存
    /// 通过缓存序列化后的 JSON 响应来降低高频健康检查请求的开销
    /// </summary>
    private sealed class HealthCheckResponseCache
    {
        private string? _cachedResponse;
        private int _cachedStatusCode;
        private DateTimeOffset _cacheExpiry;
        private int _cacheDurationSeconds;
        private readonly object _lock = new();

        /// <summary>
        /// 设置缓存时长
        /// </summary>
        public void SetDuration(int cacheDurationSeconds)
        {
            _cacheDurationSeconds = cacheDurationSeconds;
        }

        /// <summary>
        /// 尝试获取缓存的响应（含状态码）
        /// </summary>
        /// <returns>缓存的 JSON 响应字符串和状态码，如果缓存过期或未设置则返回 null</returns>
        public (string Response, int StatusCode)? TryGetCachedResponse()
        {
            if (_cacheDurationSeconds <= 0) return null;

            lock (_lock)
            {
                if (_cachedResponse != null && DateTimeOffset.UtcNow < _cacheExpiry)
                {
                    return (_cachedResponse, _cachedStatusCode);
                }
                return null;
            }
        }

        /// <summary>
        /// 更新缓存响应（含状态码）
        /// </summary>
        public void UpdateCache(string response, int statusCode)
        {
            if (_cacheDurationSeconds <= 0) return;

            lock (_lock)
            {
                _cachedResponse = response;
                _cachedStatusCode = statusCode;
                _cacheExpiry = DateTimeOffset.UtcNow.AddSeconds(_cacheDurationSeconds);
            }
        }
    }
}
