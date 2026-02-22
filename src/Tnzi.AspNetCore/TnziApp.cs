
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
    ///         options.AutoInitializeDatabase = true;
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
    /// </example>
    public static async Task<WebApplication> CreateAsync<TStartupModule>(
        string[] args,
        Action<TnziOptions>? configureOptions = null,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Func<WebApplication, Task>? configureApp = null)
        where TStartupModule : ITnziModule
    {
        var builder = WebApplication.CreateBuilder(args);

        // 配置 Tnzi 选项
        if (configureOptions != null)
        {
            builder.Services.Configure(configureOptions);
        }

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

        // 配置 Tnzi 选项
        if (configureOptions != null)
        {
            builder.Services.Configure(configureOptions);
        }

        return await BuildAndInitializeAsync<TStartupModule>(builder, configureApp);
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

        // 允许用户自定义 app 配置
        if (configureApp != null)
        {
            await configureApp(app);
        }

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
        var app = await CreateAsync<TStartupModule>(args);
        await app.RunAsync();
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
    ///     options => options.AutoInitializeDatabase = true
    /// );
    /// </code>
    /// </example>
    public static async Task RunAsync<TStartupModule>(
        string[] args,
        Action<TnziOptions>? configureOptions = null,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Func<WebApplication, Task>? configureApp = null)
        where TStartupModule : ITnziModule
    {
        var app = await CreateAsync<TStartupModule>(args, configureOptions, configureBuilder, configureApp);
        await app.RunAsync();
    }
}
