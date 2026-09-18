
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
            healthChecksBuilder.AddCheck<CacheHealthCheck>(
                "cache",
                HealthStatus.Degraded,
                ["cache", "infrastructure"],
                timeout);
        }

        // 添加数据库健康检查（支持多 DbContext）
        if (options.EnableDatabaseCheck)
        {
            var dbContextTypes = new RegisteredDbContextTypes();
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
                healthChecksBuilder.AddCheck<DatabaseHealthCheck>(
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
                healthChecksBuilder.AddCheck<RedisHealthCheck>(
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
                healthChecksBuilder.AddCheck<EventBusHealthCheck>(
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

        // 完整健康检查端点
        var healthCheckOptions = new HealthCheckOptions();
        if (options.DetailedOutput)
        {
            healthCheckOptions.ResponseWriter = (httpContext, report) =>
                WriteDetailedResponseAsync(httpContext, report, options.ExposeErrorDetails);
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
            readinessOptions.ResponseWriter = (httpContext, report) =>
                WriteDetailedResponseAsync(httpContext, report, options.ExposeErrorDetails);
        }
        webApp.MapHealthChecks(options.ReadinessPath, readinessOptions);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 输出详细的健康检查结果（JSON 格式）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 异常消息与检查项 <c>data</c> 只在 <see cref="HealthChecksOptions.ExposeErrorDetails"/>
    /// 打开时才输出。此前它们随详细输出一起、默认发给<b>匿名</b>调用方：一次数据库连接失败
    /// 会把连接串片段送出去，而文档承诺的「仅在非生产环境」在源码里没有任何东西去兑现。
    /// </para>
    /// <para>
    /// ★ <b>这里绝不能碰 <c>Response.StatusCode</c>，也不能缓存。</b>ASP.NET Core 的 HealthCheckMiddleware
    /// 先跑完全部检查、按结果设好状态码、最后才调本方法：此前挂在这里的「响应缓存」命中时
    /// 一点工作都没省（检查早就跑完了），却把刚算出来的 503 改写成上一轮缓存的 200 ——
    /// 就绪探针在最长 CacheDurationSeconds 内继续说「好」，编排器继续把流量送进依赖已挂的实例
    /// （2026-09-12 删除）。缓存的是答案不是工作，而探针恰恰不能答旧答案。
    /// </para>
    /// </remarks>
    private static Task WriteDetailedResponseAsync(HttpContext context, HealthReport report, bool exposeErrorDetails)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(BuildDetailedPayload(report, exposeErrorDetails));
    }

    /// <summary>
    /// 构造详细输出的 JSON 负载。
    /// </summary>
    /// <remarks>
    /// 单独抽出来是为了能在没有 HTTP 管线的情况下断言「默认不外泄异常消息」——
    /// 那条纪律此前只写在注释里，没有任何东西守着它。
    /// </remarks>
    internal static string BuildDetailedPayload(HealthReport report, bool exposeErrorDetails)
    {
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
                data = exposeErrorDetails && e.Value.Data.Count > 0 ? e.Value.Data : null,
                exception = exposeErrorDetails ? e.Value.Exception?.Message : null
            })
        };

        return JsonSerializer.Serialize(result, DetailedResponseJsonOptions);
    }
}
