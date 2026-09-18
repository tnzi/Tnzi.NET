namespace Tnzi.Hangfire;

/// <summary>
/// Hangfire 后台任务调度模块
/// 配置路径：Hangfire
/// </summary>
/// <remarks>
/// ★ <c>[OptionalDependsOn(typeof(AspNetCoreModule))]</c> 只为<b>排序</b>：本模块是 Infrastructure（绝对序 150），
/// AspNetCore 是 Framework（200），没有这条边时 <c>UseHangfireDashboard</c> 会排在 <c>UseAuthentication()</c>
/// 之前，角色过滤器看到的永远是匿名主体，配好角色的管理员也拿 401（2026-09-12 修复）。
/// 用 Optional 而不是硬依赖：非 web 宿主（Worker）加载本模块不该被迫拉进整个 AspNetCore 模块。
/// </remarks>
[OptionalDependsOn(typeof(AspNetCoreModule))]
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
        context.Services.AddTnziOptions<HangfireOptions, HangfireOptionsValidator>(context.Configuration);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 配置服务
    /// </summary>
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, HangfirePermissions>();

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
    /// 非开发环境用内存存储时告警。
    /// </summary>
    /// <remarks>
    /// 走 <see cref="ILogger"/> 而不是 <c>Console.WriteLine</c>：后者绕开整条日志管线，
    /// 结构化日志、级别过滤与集中采集全都收不到 —— 一条只出现在本机控制台的告警，
    /// 在真正需要它的部署里等于不存在。判据也从直接读 <c>ASPNETCORE_ENVIRONMENT</c>
    /// 改成 <see cref="IHostEnvironment"/>（宿主可能用别的方式设置环境）。
    /// </remarks>
    private static void WarnAboutInMemoryStorage(ApplicationInitializationContext context, HangfireOptions options)
    {
        if (options.StorageType != StorageType.Memory)
            return;

        var environment = context.ServiceProvider.GetService<IHostEnvironment>();
        if (environment?.IsDevelopment() == true)
            return;

        var logger = context.ServiceProvider.GetService<ILogger<HangfireModule>>();
        logger?.LogWarning(
            "Hangfire is using in-memory storage in the '{Environment}' environment. " +
            "Every scheduled and background job is lost on restart. " +
            "Configure Redis, SQL Server or PostgreSQL storage for production.",
            environment?.EnvironmentName ?? "Production");
    }

    /// <summary>
    /// 配置存储
    /// </summary>
    private void ConfigureStorage(IGlobalConfiguration config, HangfireOptions options)
    {
        switch (options.StorageType)
        {
            case StorageType.Memory:
                // 「非开发环境用内存存储」的告警推迟到 OnApplicationInitializationAsync 发：
                // 这里还没有容器，拿不到 ILogger，而 Console.WriteLine 绕开了整条日志管线 ——
                // 结构化日志、日志级别、集中采集全都收不到它，等于这条告警只存在于本机控制台。
                config.UseInMemoryStorage();
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
        var options = context.ServiceProvider.GetRequiredService<IOptions<HangfireOptions>>().Value;
        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        // Eagerly initialize Hangfire's global JobStorage.Current during application initialization.
        //
        // Hangfire's static facade (BackgroundJob.*, RecurringJob.*, JobStorage.Current) reads the
        // global JobStorage.Current, which AddHangfire only assigns the first time the JobStorage
        // service is resolved. With AddHangfireServer (hosted-service model) that first resolution
        // happens when the server's IHostedService starts: AFTER app.Run(), i.e. AFTER every
        // module's OnApplicationInitializationAsync has already run. The Dashboard middleware below
        // would also trigger it, but only when the Dashboard is enabled. As a result, any module that
        // registers a recurring job at init time via IBackgroundJobManager.CreateRecurring (which
        // calls the static RecurringJob.AddOrUpdate) would throw "JobStorage.Current ... has not been
        // initialized" whenever the Dashboard is disabled. Resolving JobStorage here runs that
        // configuration now and assigns the global instance, making the static API usable for the
        // remaining modules' initialization regardless of host type or Dashboard state.
        JobStorage.Current = context.ServiceProvider.GetRequiredService<JobStorage>();

        WarnAboutInMemoryStorage(context, options);

        var app = context.App;
        if (app == null)
        {
            return Task.CompletedTask;
        }

        // 配置 Dashboard。验证器保证：开着 Dashboard 就一定带授权与非空角色表，这里不再有「无授权」分支。
        // ★ 挂在认证之后（见类注释的 OptionalDependsOn）：过滤器读的是 HttpContext.User。
        if (options.Dashboard.Enabled)
        {
            var dashboardOptions = new DashboardOptions
            {
                Authorization =
                [
                    new DashboardRoleAuthorizationFilter(options.Dashboard.AllowedRoles.ToArray())
                ]
            };

            app.UseHangfireDashboard(options.Dashboard.Path, dashboardOptions);
        }

        return Task.CompletedTask;
    }
}
