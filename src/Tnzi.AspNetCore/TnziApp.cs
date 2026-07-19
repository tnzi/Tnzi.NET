
namespace Tnzi;

/// <summary>
/// Tnzi 快捷启动类
/// 提供一行代码启动应用的便捷方式
/// </summary>
public static class TnziApp
{
    /// <summary>
    /// 创建并配置 Tnzi 应用程序
    /// </summary>
    /// <typeparam name="TStartupModule">启动模块类型</typeparam>
    /// <param name="args">命令行参数</param>
    /// <returns>已配置的 WebApplication</returns>
    /// <example>
    /// <code>
    /// var app = await TnziApp.CreateAsync&lt;StartupModule&gt;(args);
    /// await app.RunAsync();
    /// </code>
    /// </example>
    public static async Task<WebApplication> CreateAsync<TStartupModule>(string[] args)
        where TStartupModule : ITnziModule
    {
        return await CreateAsync<TStartupModule>(args, null, null, null);
    }

    /// <summary>
    /// 创建并配置 Tnzi 应用程序（带自定义配置）
    /// </summary>
    /// <typeparam name="TStartupModule">启动模块类型</typeparam>
    /// <param name="args">命令行参数</param>
    /// <param name="configureBuilder">自定义 WebApplicationBuilder 配置</param>
    /// <param name="configureApp">自定义 WebApplication 配置</param>
    /// <returns>已配置的 WebApplication</returns>
    public static async Task<WebApplication> CreateAsync<TStartupModule>(
        string[] args,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Func<WebApplication, Task>? configureApp = null)
        where TStartupModule : ITnziModule
    {
        return await CreateAsync<TStartupModule>(args, null, configureBuilder, configureApp);
    }

    /// <summary>
    /// 创建并配置 Tnzi 应用程序（带完整配置选项）
    /// </summary>
    /// <typeparam name="TStartupModule">启动模块类型</typeparam>
    /// <param name="args">命令行参数</param>
    /// <param name="configureOptions">配置 Tnzi 选项</param>
    /// <param name="configureBuilder">自定义 WebApplicationBuilder 配置</param>
    /// <param name="configureApp">自定义 WebApplication 配置</param>
    /// <returns>已配置的 WebApplication</returns>
    /// <example>
    /// <code>
    /// var app = await TnziApp.CreateAsync&lt;StartupModule&gt;(
    ///     args,
    ///     options => {
    ///         options.EnableDiagnostics = true;
    ///     },
    ///     builder => {
    ///         // 自定义 builder 配置
    ///     },
    ///     async app => {
    ///         // 自定义 app 配置
    ///     }
    /// );
    /// </code>
    /// TnziOptions 也可经 "Tnzi" 配置节设置（代码回调优先），如 appsettings.json:
    /// { "Tnzi": { "AutoInitializeDatabase": false } }
    /// </example>
    public static async Task<WebApplication> CreateAsync<TStartupModule>(
        string[] args,
        Action<TnziOptions>? configureOptions = null,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Func<WebApplication, Task>? configureApp = null)
        where TStartupModule : ITnziModule
    {
        var builder = WebApplication.CreateBuilder(args);

        // 配置 Tnzi 选项（"Tnzi" 配置节 + 代码回调）
        ConfigureTnziOptions(builder, configureOptions);

        // 允许用户自定义 builder 配置
        configureBuilder?.Invoke(builder);

        return await BuildAndInitializeAsync<TStartupModule>(builder, configureApp);
    }

    /// <summary>
    /// 从现有的 WebApplicationBuilder 创建并配置 Tnzi 应用程序
    /// </summary>
    /// <typeparam name="TStartupModule">启动模块类型</typeparam>
    /// <param name="builder">WebApplicationBuilder 实例</param>
    /// <param name="configureOptions">配置 Tnzi 选项</param>
    /// <param name="configureApp">自定义 WebApplication 配置</param>
    /// <returns>已配置的 WebApplication</returns>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// // 自定义 builder 配置...
    /// var app = await TnziApp.CreateAsync&lt;StartupModule&gt;(builder);
    /// </code>
    /// </example>
    public static async Task<WebApplication> CreateAsync<TStartupModule>(
        WebApplicationBuilder builder,
        Action<TnziOptions>? configureOptions = null,
        Func<WebApplication, Task>? configureApp = null)
        where TStartupModule : ITnziModule
    {
        Check.NotNull(builder);

        // 配置 Tnzi 选项（"Tnzi" 配置节 + 代码回调）
        ConfigureTnziOptions(builder, configureOptions);

        return await BuildAndInitializeAsync<TStartupModule>(builder, configureApp);
    }

    /// <summary>
    /// 注册 TnziOptions：先绑定 "Tnzi" 配置节（appsettings 可按环境覆盖，如
    /// "Tnzi": { "AutoInitializeDatabase": false }），再应用代码回调（代码优先于配置）。
    /// </summary>
    private static void ConfigureTnziOptions(WebApplicationBuilder builder, Action<TnziOptions>? configureOptions)
    {
        builder.Services.Configure<TnziOptions>(builder.Configuration.GetSection("Tnzi"));

        if (configureOptions != null)
        {
            builder.Services.Configure(configureOptions);
        }
    }

    /// <summary>
    /// 构建并初始化 Tnzi 应用程序（共享的 Build → Log → InitDB → ConfigureApp 逻辑）
    /// </summary>
    private static async Task<WebApplication> BuildAndInitializeAsync<TStartupModule>(
        WebApplicationBuilder builder,
        Func<WebApplication, Task>? configureApp)
        where TStartupModule : ITnziModule
    {
        // 添加 Tnzi.NET 框架
        var tnziApp = await builder.Services.AddTnziAsync<TStartupModule>(builder.Configuration);

        var app = builder.Build();

        // 记录启动信息
        var loggerFactory = app.Services.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger("Tnzi.Startup");
        logger?.LogInformation("Tnzi framework initialized with {ModuleCount} module(s)", tnziApp.Modules.Count);

        // 获取 Tnzi 选项，决定是否自动初始化数据库
        var options = app.Services.GetService<IOptions<TnziOptions>>()?.Value ?? new TnziOptions();

        // 根据选项决定初始化方式
        if (options.AutoInitializeDatabase)
        {
            logger?.LogInformation("Auto-initializing database (AutoInitializeDatabase = true)");
            await app.UseTnziWithDefaultsAsync();
        }
        else
        {
            await app.UseTnziAsync();
        }

        // Post-migration startup tasks — run on EVERY boot, AFTER module init +
        // (optional) migration, regardless of the AutoInitializeDatabase / prod-skip
        // gates. Framework infrastructure that needs the migrated schema (e.g. the
        // permission-catalogue sync) registers an IPostMigrationStartupTask instead of
        // doing DB work in module init, which runs before migrations and fails on an
        // empty database (the old "boot twice" bug). No-op when none are registered.
        await app.RunPostMigrationStartupTasksAsync();

        // 允许用户自定义 app 配置
        if (configureApp != null)
        {
            await configureApp(app);
        }

        // 启动成功，清理旧的错误日志文件
        CleanupStartupErrorLog();

        logger?.LogInformation("Tnzi application created and configured successfully");

        return app;
    }

    /// <summary>
    /// 创建、配置并运行 Tnzi 应用程序
    /// </summary>
    /// <typeparam name="TStartupModule">启动模块类型</typeparam>
    /// <param name="args">命令行参数</param>
    /// <example>
    /// <code>
    /// await TnziApp.RunAsync&lt;StartupModule&gt;(args);
    /// </code>
    /// </example>
    public static async Task RunAsync<TStartupModule>(string[] args)
        where TStartupModule : ITnziModule
    {
        try
        {
            var app = await CreateAsync<TStartupModule>(args);
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            throw;
        }
    }

    /// <summary>
    /// 创建、配置并运行 Tnzi 应用程序（带配置选项）
    /// </summary>
    /// <typeparam name="TStartupModule">启动模块类型</typeparam>
    /// <param name="args">命令行参数</param>
    /// <param name="configureOptions">配置 Tnzi 选项</param>
    /// <param name="configureBuilder">自定义 WebApplicationBuilder 配置</param>
    /// <param name="configureApp">自定义 WebApplication 配置</param>
    /// <example>
    /// <code>
    /// await TnziApp.RunAsync&lt;StartupModule&gt;(
    ///     args,
    ///     options => options.EnableDiagnostics = true
    /// );
    /// </code>
    /// TnziOptions 也可经 "Tnzi" 配置节设置（代码回调优先）。
    /// </example>
    public static async Task RunAsync<TStartupModule>(
        string[] args,
        Action<TnziOptions>? configureOptions = null,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Func<WebApplication, Task>? configureApp = null)
        where TStartupModule : ITnziModule
    {
        try
        {
            var app = await CreateAsync<TStartupModule>(args, configureOptions, configureBuilder, configureApp);
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            throw;
        }
    }

    private const string StartupErrorLogFileName = "startup-error.log";

    /// <summary>
    /// 格式化输出启动错误到控制台和文件
    /// </summary>
    private static void WriteStartupError(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        sb.AppendLine();
        sb.AppendLine("========================================================");
        sb.AppendLine("  Tnzi Application Startup Failed");
        sb.AppendLine("========================================================");
        sb.AppendLine($"  Time:    {timestamp}");

        switch (ex)
        {
            case Modules.ModuleCircularDependencyException circularEx:
                sb.AppendLine($"  Error:   Module circular dependency detected");
                sb.AppendLine($"  Path:    {string.Join(" -> ", circularEx.CyclePath.Select(t => t.Name))}");
                sb.AppendLine($"  Detail:  {circularEx.Message}");
                break;

            case Modules.ModuleMissingDependencyException missingEx:
                sb.AppendLine($"  Module:  {missingEx.ModuleType.Name}");
                sb.AppendLine($"  Error:   Missing required dependencies");
                sb.AppendLine($"  Missing: {string.Join(", ", missingEx.MissingDependencies.Select(t => t.Name))}");
                break;

            case Modules.ModuleException moduleEx:
                sb.AppendLine($"  Module:  {moduleEx.ModuleType.Name}");
                sb.AppendLine($"  Phase:   {moduleEx.Phase}");
                sb.AppendLine($"  Error:   {moduleEx.InnerException?.Message ?? moduleEx.Message}");
                if (moduleEx.InnerException?.InnerException != null)
                {
                    sb.AppendLine($"  Cause:   {moduleEx.InnerException.InnerException.Message}");
                }
                break;

            default:
                sb.AppendLine($"  Error:   {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    sb.AppendLine($"  Cause:   {ex.InnerException.Message}");
                }
                break;
        }

        sb.AppendLine("========================================================");
        sb.AppendLine();

        var message = sb.ToString();

        // 控制台输出（带颜色高亮）
        try
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.Write(message);
            Console.ForegroundColor = originalColor;
        }
        catch
        {
            // 控制台不可用时静默忽略（如某些 Service 场景）
            Console.Error.Write(message);
        }

        // 写入 startup-error.log（IIS/Docker/Service 场景保底）
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, StartupErrorLogFileName);
            var logContent = $"{message}--- Full Stack Trace ---\n{ex}\n";
            File.WriteAllText(logPath, logContent);
        }
        catch
        {
            // 文件写入失败时静默忽略（权限不足等）
        }
    }

    /// <summary>
    /// 启动成功时清理旧的错误日志文件
    /// </summary>
    private static void CleanupStartupErrorLog()
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, StartupErrorLogFileName);
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
        catch
        {
            // 清理失败不影响正常运行
        }
    }
}
