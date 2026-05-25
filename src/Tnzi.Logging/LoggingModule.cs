
namespace Tnzi.Logging;

/// <summary>
/// 日志模块
/// 基于 Serilog 提供结构化日志记录功能
/// 配置路径：Logging
/// </summary>
public class LoggingModule : TnziInfrastructureModule
{
    /// <summary>
    /// 日志模块最先加载
    /// </summary>
    public override int LoadOrder => 0;

    /// <summary>
    /// 预配置服务
    /// </summary>
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用启动时验证
        context.Services.AddOptions<LoggingOptions>()
            .Bind(context.Configuration.GetSection("Logging"))
            .ValidateWith<LoggingOptions, LoggingOptionsValidator>();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 配置服务
    /// </summary>
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var configuration = context.Configuration;
        var options = configuration.GetSection("Logging").Get<LoggingOptions>() ?? new LoggingOptions();

        // 开发环境默认启用 Debug 日志
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (env == "Development" && !options.FileOutput.Debug.Enabled)
        {
            options.FileOutput.Debug.Enabled = true;
        }

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(options.MinimumLevel)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "Tnzi");

        // 控制台输出
        if (options.EnableConsole)
        {
            loggerConfig.WriteTo.Console();
        }

        // 文件输出
        if (options.EnableFile)
        {
            var baseLogPath = options.BasePath;

            // 自动创建日志目录，避免 Serilog 因目录不存在而抛出异常
            EnsureLogDirectoriesCreated(baseLogPath, options.FileOutput);

            // 配置 shared: true 允许文件在写入时被删除（类似 Log4j 的行为）
            // 这样即使应用正在写入日志，文件也可以被其他进程删除
            // 配置 flushToDiskInterval 定期刷新到磁盘，确保日志及时写入

            // Information级别日志
            if (options.FileOutput.Information.Enabled)
            {
                loggerConfig.WriteTo.File(
                    Path.Combine(baseLogPath, "Information", "log-.txt"),
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.FileOutput.Information.RetainedFileCountLimit,
                    shared: true,
                    flushToDiskInterval: options.FlushToDiskInterval);
            }

            // Warning级别日志
            if (options.FileOutput.Warning.Enabled)
            {
                loggerConfig.WriteTo.File(
                    Path.Combine(baseLogPath, "Warning", "log-.txt"),
                    restrictedToMinimumLevel: LogEventLevel.Warning,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.FileOutput.Warning.RetainedFileCountLimit,
                    shared: true,
                    flushToDiskInterval: options.FlushToDiskInterval);
            }

            // Error级别日志
            if (options.FileOutput.Error.Enabled)
            {
                loggerConfig.WriteTo.File(
                    Path.Combine(baseLogPath, "Error", "log-.txt"),
                    restrictedToMinimumLevel: LogEventLevel.Error,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.FileOutput.Error.RetainedFileCountLimit,
                    shared: true,
                    flushToDiskInterval: options.FlushToDiskInterval);
            }

            // Fatal级别日志
            if (options.FileOutput.Fatal.Enabled)
            {
                loggerConfig.WriteTo.File(
                    Path.Combine(baseLogPath, "Fatal", "log-.txt"),
                    restrictedToMinimumLevel: LogEventLevel.Fatal,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.FileOutput.Fatal.RetainedFileCountLimit,
                    shared: true,
                    flushToDiskInterval: options.FlushToDiskInterval);
            }

            // Debug级别日志
            if (options.FileOutput.Debug.Enabled)
            {
                loggerConfig.WriteTo.File(
                    Path.Combine(baseLogPath, "Debug", "log-.txt"),
                    restrictedToMinimumLevel: LogEventLevel.Debug,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.FileOutput.Debug.RetainedFileCountLimit,
                    shared: true,
                    flushToDiskInterval: options.FlushToDiskInterval);
            }
        }

        Log.Logger = loggerConfig.CreateLogger();
        context.Services.AddSerilog();

        // Read-only admin access to the on-disk log files. Powers the
        // `admin/logs/*` controller exposed by Tnzi.AspNetCore.
        context.Services.AddSingleton<Services.Interfaces.ILogFileService, Services.LogFileService>();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 根据启用的日志级别，自动创建对应的日志子目录
    /// </summary>
    private static void EnsureLogDirectoriesCreated(string basePath, FileOutputOptions fileOutput)
    {
        // 收集所有启用的日志级别对应的子目录名
        var levelDirectories = new List<string>();

        if (fileOutput.Information.Enabled)
            levelDirectories.Add("Information");

        if (fileOutput.Warning.Enabled)
            levelDirectories.Add("Warning");

        if (fileOutput.Error.Enabled)
            levelDirectories.Add("Error");

        if (fileOutput.Fatal.Enabled)
            levelDirectories.Add("Fatal");

        if (fileOutput.Debug.Enabled)
            levelDirectories.Add("Debug");

        // 逐个创建目录（Directory.CreateDirectory 会递归创建所有中间目录）
        foreach (var levelDir in levelDirectories)
        {
            var fullPath = Path.Combine(basePath, levelDir);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
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

        // 从服务提供者获取配置
        var configuration = context.ServiceProvider.GetRequiredService<IConfiguration>();
        var options = configuration.GetSection("Logging").Get<LoggingOptions>() ?? new LoggingOptions();

        // 启用请求日志
        if (options.RequestLogging.Enabled)
        {
            if (!string.IsNullOrEmpty(options.RequestLogging.MessageTemplate))
            {
                app.UseSerilogRequestLogging(opts =>
                {
                    opts.MessageTemplate = options.RequestLogging.MessageTemplate;
                    // elapsed 参数单位为毫秒，SlowRequestThresholdSeconds 单位为秒，需转换
                    var slowThresholdMs = options.RequestLogging.SlowRequestThresholdSeconds * 1000;
                    opts.GetLevel = (httpContext, elapsed, ex) =>
                        ex != null
                            ? LogEventLevel.Error
                            : elapsed > slowThresholdMs
                                ? LogEventLevel.Warning
                                : options.RequestLogging.Level;
                });
            }
            else
            {
                app.UseSerilogRequestLogging();
            }
        }

        return Task.CompletedTask;
    }

    public override Task OnApplicationShutdownAsync(ApplicationShutdownContext context)
    {
        // 关闭并刷新 Serilog，释放日志文件句柄
        // 这确保日志文件可以被删除，避免文件占用问题
        Log.CloseAndFlush();

        return Task.CompletedTask;
    }
}