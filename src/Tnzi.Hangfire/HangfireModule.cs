namespace Tnzi.Hangfire;

/// <summary>
/// Hangfire 后台任务调度模块
/// 配置路径：Hangfire
/// </summary>
public class HangfireModule : TnziInfrastructureModule
{
    /// <summary>
    /// 后台任务模块加载顺序
    /// </summary>
    public override int LoadOrder => 50;

    /// <summary>
    /// 预配置服务
    /// </summary>
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用启动时验证
        context.Services.AddOptions<HangfireOptions>()
            .Bind(context.Configuration.GetSection("Hangfire"))
            .ValidateWith<HangfireOptions, HangfireOptionsValidator>();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 配置服务
    /// </summary>
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var configuration = context.Configuration;
        var options = configuration.GetSection("Hangfire").Get<HangfireOptions>() ?? new HangfireOptions();

        // 展开 ${VAR} 占位符（如有）
        if (options.ConnectionString != null)
            options.ConnectionString = ConnectionStringExpander.Expand(options.ConnectionString, configuration);

        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        // 配置 Hangfire 存储
        services.AddHangfire(config =>
        {
            ConfigureStorage(config, options);
        });

        // 配置 Hangfire 服务器
        services.AddHangfireServer(serverOptions =>
        {
            serverOptions.WorkerCount = options.Server.WorkerCount;
            if (!string.IsNullOrEmpty(options.Server.ServerName))
            {
                serverOptions.ServerName = options.Server.ServerName;
            }
            serverOptions.Queues = options.Server.Queues.ToArray();
        });

        // 注册 IBackgroundJobManager
        services.AddSingleton<IBackgroundJobManager, HangfireBackgroundJobManager>();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 配置存储
    /// </summary>
    private void ConfigureStorage(IGlobalConfiguration config, HangfireOptions options)
    {
        switch (options.StorageType)
        {
            case StorageType.Memory:
                config.UseInMemoryStorage();
                var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                if (!string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase))
                {
                    // 非开发环境使用内存存储时输出警告
                    Console.WriteLine(
                        $"WARNING: Hangfire is using in-memory storage in '{env ?? "Production"}' environment. " +
                        "All background jobs will be lost on application restart. " +
                        "Consider using Redis, SQL Server, or PostgreSQL for production.");
                }
                break;

            case StorageType.Redis:
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    throw new ConfigurationException("Hangfire.ConnectionString",
                        "ConnectionString is required when using Redis storage.");
                }
                config.UseRedisStorage(options.ConnectionString);
                break;

            case StorageType.SqlServer:
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    throw new ConfigurationException("Hangfire.ConnectionString",
                        "ConnectionString is required when using SQL Server storage.");
                }
                config.UseSqlServerStorage(options.ConnectionString);
                break;

            case StorageType.PostgreSQL:
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    throw new ConfigurationException("Hangfire.ConnectionString",
                        "ConnectionString is required when using PostgreSQL storage.");
                }
                // Use recommended Action<PostgreSqlBootstrapperOptions> overload (Hangfire.PostgreSql 1.20.13+)
                config.UsePostgreSqlStorage(configure => configure.UseNpgsqlConnection(options.ConnectionString));
                break;

            default:
                throw new InfrastructureException("Hangfire",
                    $"Storage type '{options.StorageType}' is not supported.");
        }
    }

    /// <summary>
    /// 应用初始化
    /// </summary>
    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var app = context.App;
        if (app == null)
        {
            return Task.CompletedTask;
        }

        var options = context.ServiceProvider.GetRequiredService<IOptions<HangfireOptions>>().Value;
        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        // 配置 Dashboard
        if (options.Dashboard.Enabled)
        {
            var dashboardOptions = new DashboardOptions();

            // 配置授权（如果需要）
            if (options.Dashboard.EnableAuthorization && options.Dashboard.AllowedRoles.Count > 0)
            {
                // 使用基于角色的授权
                dashboardOptions.Authorization = new[]
                {
                    new DashboardRoleAuthorizationFilter(options.Dashboard.AllowedRoles.ToArray())
                };
            }
            else
            {
                var logger = context.ServiceProvider.GetService<ILogger<HangfireModule>>();
                logger?.LogWarning("Hangfire Dashboard authorization is disabled. Consider enabling it in production.");
            }

            app.UseHangfireDashboard(options.Dashboard.Path, dashboardOptions);
        }

        return Task.CompletedTask;
    }
}
