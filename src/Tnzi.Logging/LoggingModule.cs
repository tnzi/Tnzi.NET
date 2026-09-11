
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
    /// 默认的来源级别覆盖。消费方经 <c>Logging:MinimumLevelOverrides</c> 补充的条目
    /// 会**合并**在这份名单之上（同名键消费方胜），不是整体替换 —— 见
    /// <see cref="ResolveMinimumLevelOverrides"/>。
    ///
    /// 与 ASP.NET Core 模板 <c>Logging:LogLevel</c> 一贯写的东西一致 —— 此前 Serilog
    /// 走扁平 MinimumLevel,那份配置对它完全无效。
    /// </summary>
    private static readonly Dictionary<string, LogEventLevel> DefaultMinimumLevelOverrides =
        new(StringComparer.Ordinal)
        {
            ["Microsoft.AspNetCore"] = LogEventLevel.Warning,
        };

    /// <summary>
    /// 把消费方配置的来源级别覆盖**合并**到框架默认名单之上，同名键以消费方为准。
    ///
    /// ★ 为什么是合并而不是替换：<c>Microsoft.AspNetCore</c> → Warning 这一条不是噪音
    /// 偏好，而是一条安全控制 —— ASP.NET Core 的 <c>Hosting.Diagnostics</c> 会在
    /// Information 级把 <c>QueryString</c> 原文写进日志，而查询串里就有凭据
    /// （SignalR 传输的 <c>access_token</c>、文件签名令牌 <c>sig</c>、分享链接
    /// <c>password</c>）。整份替换意味着任何一条与它无关的覆盖
    /// （比如 <c>"MyApp.Data": "Debug"</c>）都会顺手把这条控制删掉，且**毫无症状**：
    /// 日志照常写、请求照常成功，只是从此每条带令牌的 URL 都留在磁盘上。
    ///
    /// 消费方仍然可以显式调整它 —— 把 <c>Microsoft.AspNetCore</c> 配成
    /// <c>Information</c> 就拿回那些诊断行，那是一次知情的选择。
    ///
    /// ★ 比较器刻意用 <see cref="StringComparer.Ordinal"/>：Serilog 的来源前缀匹配是
    /// 区分大小写的，用忽略大小写的比较器会让一条大小写写错的消费方配置
    /// （<c>"microsoft.aspnetcore"</c>）顶掉默认条目，而它自己又匹配不上任何来源 ——
    /// 于是两条都不生效。按 Ordinal 合并时两条并存，正确大小写的那条仍然拦得住。
    /// </summary>
    /// <param name="options">日志配置</param>
    /// <returns>生效的来源 → 最低级别映射</returns>
    public static IReadOnlyDictionary<string, LogEventLevel> ResolveMinimumLevelOverrides(LoggingOptions options)
    {
        Check.NotNull(options);

        var merged = new Dictionary<string, LogEventLevel>(DefaultMinimumLevelOverrides, StringComparer.Ordinal);
        if (options.MinimumLevelOverrides != null)
        {
            foreach (var (source, level) in options.MinimumLevelOverrides)
            {
                if (!string.IsNullOrWhiteSpace(source))
                {
                    merged[source] = level;
                }
            }
        }

        return merged;
    }

    /// <summary>
    /// 组装 logger 的**级别与扩充**部分（最低级别、来源级别覆盖、Enricher），
    /// 不含任何 sink —— sink 由 <see cref="ConfigureServicesAsync"/> 按配置追加。
    ///
    /// 单独抽出来是为了让测试能对同一份级别配置接一个内存 sink 做取证，
    /// 而不必写日志文件、也不必在测试里重抄一遍这段逻辑（重抄的话，
    /// 测试证明的就是测试自己而不是模块）。
    /// </summary>
    /// <param name="options">日志配置</param>
    /// <returns>已配置级别与 Enricher 的 <see cref="LoggerConfiguration"/></returns>
    public static LoggerConfiguration CreateBaseLoggerConfiguration(LoggingOptions options)
    {
        Check.NotNull(options);

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(options.MinimumLevel)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "Tnzi");

        foreach (var (source, level) in ResolveMinimumLevelOverrides(options))
        {
            loggerConfig.MinimumLevel.Override(source, level);
        }

        return loggerConfig;
    }

    /// <summary>
    /// 预配置服务
    /// </summary>
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用启动时验证
        context.Services.AddTnziOptions<LoggingOptions, LoggingOptionsValidator>(context.Configuration);

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

        // 最低级别 + 来源级别覆盖 + Enricher。默认把 Microsoft.AspNetCore 压到 Warning:
        // 它的 Hosting 诊断会在 Information 级把 QueryString 原文写出去,而查询串里就有
        // 凭据(SignalR 的 access_token、文件签名令牌 sig、分享链接口令 password)。
        var loggerConfig = CreateBaseLoggerConfiguration(options);

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
        context.Services.AddSingleton<ILogFileService, LogFileService>();

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
        // MessageTemplate 是可选项（留空即用 Serilog 默认模板），但 Level 与
        // SlowRequestThresholdSeconds 必须无条件生效：早先只在配了模板时才装 GetLevel，
        // 导致默认配置下这两项静默失效（慢请求不升级、Level 被忽略）。
        if (options.RequestLogging.Enabled)
        {
            app.UseSerilogRequestLogging(opts =>
            {
                if (!string.IsNullOrEmpty(options.RequestLogging.MessageTemplate))
                {
                    opts.MessageTemplate = options.RequestLogging.MessageTemplate;
                }

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