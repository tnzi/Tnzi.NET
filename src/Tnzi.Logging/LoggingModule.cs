
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

        // ★ 把经 Microsoft.Extensions.Logging 注册的 ILoggerProvider 桥进 Serilog 管线。
        // 不带 providers 的 AddSerilog() 会让 SerilogLoggerFactory.AddProvider 成为空实现：
        // 容器里每一个 ILoggerProvider（OpenTelemetry 的日志导出器、Application Insights、
        // 消费方自己注册的）都被静默忽略 —— 启动照常、文件照写，只是那一路一条都收不到。
        // 桥接后事件先过 Serilog 的最低级别与来源覆盖，再分发给每个 provider。
        var providers = new LoggerProviderCollection();
        loggerConfig.WriteTo.Providers(providers);

        // 桥接不是对每个 provider 都成立，摘掉三类（理由各不相同，见 ProvidersNotBridged）：
        // 宿主预注册的 Console（Serilog 的 Console sink 已替代它，桥接后控制台写两遍）、
        // Debug / EventLog（本模块没有等价 sink，桥接之前它们就一条都没收到过，
        // 保持不投递；要这两路输出的消费方自己加对应的 Serilog sink）、
        // 以及消费方顺手 builder.Logging.AddSerilog() 注册的 SerilogLoggerProvider
        // （它把事件写回 Log.Logger，桥接后成回路）。EventSource 没有 Serilog 等价物
        // （dotnet-trace / dotnet-monitor 靠它拿日志），留下。其它 provider 一律保留并转发。
        RemoveProvidersNotBridged(context.Services);

        Log.Logger = loggerConfig.CreateLogger();
        context.Services.AddSerilog(providers: providers);

        // Read-only admin access to the on-disk log files. Powers the
        // `admin/logs/*` controller exposed by Tnzi.AspNetCore.
        context.Services.AddSingleton<ILogFileService, LogFileService>();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 不桥进 Serilog 管线的 ILoggerProvider，按实现类型精确匹配（不按命名空间：消费方经
    /// <c>builder.Logging</c> 加的别家 provider，Application Insights、Seq 等，正是要桥接的对象，
    /// 不能被顺手摘掉）。三类各有各的理由：
    /// <list type="bullet">
    /// <item><see cref="ConsoleLoggerProvider"/>：Serilog 的 Console sink 已替代它，桥接后控制台写两遍。</item>
    /// <item><see cref="DebugLoggerProvider"/> / <see cref="EventLogLoggerProvider"/>：本模块没有等价的
    /// Debug / Event Log sink，桥接之前它们就一条都没收到过（<c>AddSerilog()</c> 不带 providers 时
    /// 全部 provider 都被忽略），这里保持不投递而不是开始投递；要这两路输出的消费方自己加对应的 Serilog sink。</item>
    /// <item><see cref="SerilogLoggerProvider"/>：消费方在 Program.cs 里顺手 <c>builder.Logging.AddSerilog()</c>
    /// 留下的。它把每条事件写回 <see cref="Log.Logger"/>，而那正是装着 Providers sink 的管线：
    /// 桥接后事件 → Providers sink → SerilogLoggerProvider → Log.Logger → Providers sink 无穷递归，
    /// 第一条日志就栈溢出。桥接之前这一注册是惰性的，所以它是本模块接管桥之后唯一会从
    /// 「多余」变成「致命」的注册。MEL 与 Serilog 之间的桥由本模块独占。</item>
    /// </list>
    /// </summary>
    private static readonly Type[] ProvidersNotBridged =
    [
        typeof(ConsoleLoggerProvider),
        typeof(DebugLoggerProvider),
        typeof(EventLogLoggerProvider),
        typeof(SerilogLoggerProvider)
    ];

    /// <summary>
    /// 从服务集合里移除 <see cref="ProvidersNotBridged"/> 的注册。
    /// 不用 <c>ClearProviders()</c>：那会把消费方在本模块之前注册的 provider 一并清掉，
    /// 与本次要修的缺陷是同一种失效形态（provider 注册了、事件一条都没有、零症状）。
    /// </summary>
    private static void RemoveProvidersNotBridged(IServiceCollection services)
    {
        var notBridged = services
            .Where(d => d.ServiceType == typeof(ILoggerProvider)
                        && !d.IsKeyedService
                        && GetImplementationType(d) is { } implementationType
                        && ProvidersNotBridged.Contains(implementationType))
            .ToList();

        foreach (var descriptor in notBridged)
        {
            services.Remove(descriptor);
        }
    }

    /// <summary>
    /// 描述符的实现类型。宿主默认 provider 按类型注册，而 <c>builder.Logging.AddSerilog()</c>
    /// 注册的是实例（<c>dispose: true</c> 时是工厂）—— 只看 <c>ImplementationType</c> 会漏掉后两种，
    /// 于是正是那个必须摘掉的 provider 留在了桥里。工厂形态照 <see cref="ServiceDescriptor"/> 自己的
    /// 做法读委托的泛型实参；返回 <c>ILoggerProvider</c> 这类接口的工厂不可知，不匹配即保留并桥接。
    /// </summary>
    private static Type? GetImplementationType(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationType != null)
        {
            return descriptor.ImplementationType;
        }

        if (descriptor.ImplementationInstance != null)
        {
            return descriptor.ImplementationInstance.GetType();
        }

        var factoryTypeArguments = descriptor.ImplementationFactory?.GetType().GenericTypeArguments;
        return factoryTypeArguments is { Length: 2 } ? factoryTypeArguments[1] : null;
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