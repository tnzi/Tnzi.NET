
namespace Tnzi.OpenTelemetry;

/// <summary>
/// OpenTelemetry 可观测性模块
/// 提供分布式追踪、指标收集和日志导出功能
/// 配置路径：OpenTelemetry
/// </summary>
[DependsOn(typeof(AspNetCoreModule))]
public class OpenTelemetryModule : TnziInfrastructureModule
{
    /// <summary>
    /// 在 AspNetCore 模块之后加载
    /// </summary>
    public override int LoadOrder => 10;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用启动时验证
        context.Services.AddTnziOptions<OpenTelemetryOptions, OpenTelemetryOptionsValidator>(context.Configuration);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Well-known framework ActivitySource names (Tnzi.AI, Tnzi.AI.Rag, Tnzi.Framework)
    /// These are auto-subscribed when InstrumentFramework is true
    /// </summary>
    private static readonly string[] FrameworkActivitySourceNames =
    [
        "Tnzi.AI",
        "Tnzi.AI.Rag",
        "Tnzi.Framework"
    ];

    /// <summary>
    /// Well-known framework Meter names (matching ActivitySource names)
    /// These are auto-subscribed when InstrumentFramework is true
    /// </summary>
    private static readonly string[] FrameworkMeterNames =
    [
        "Tnzi.AI",
        "Tnzi.AI.Rag",
        "Tnzi.Framework"
    ];

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var config = context.Configuration.GetSection("OpenTelemetry").Get<OpenTelemetryOptions>()
            ?? new OpenTelemetryOptions();

        // 如果没有配置服务名称，使用入口程序集名称
        var serviceName = config.ServiceName
            ?? Assembly.GetEntryAssembly()?.GetName().Name
            ?? "TnziApp";

        var serviceVersion = config.ServiceVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? "1.0.0";

        // 配置 OpenTelemetry（资源信息统一配置，Tracing/Metrics/Logging 共享）
        var otelBuilder = context.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion,
                serviceInstanceId: config.ServiceInstanceId));

        // Collect framework and additional source names
        var activitySourceNames = CollectActivitySourceNames(config);
        var meterNames = CollectMeterNames(config);

        // 配置 Tracing
        if (config.EnableTracing)
        {
            otelBuilder.WithTracing(tracing =>
            {
                // 设置采样率（使用 ParentBasedSampler 包装以尊重父级采样决策）
                if (config.SamplingRatio < 1.0)
                {
                    tracing.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(config.SamplingRatio)));
                }

                // Subscribe to framework and custom ActivitySources
                if (activitySourceNames.Length > 0)
                {
                    tracing.AddSource(activitySourceNames);
                }

                // ASP.NET Core 追踪
                if (config.InstrumentAspNetCore)
                {
                    tracing.AddAspNetCoreInstrumentation(ConfigureAspNetCoreTracing);
                }

                // HTTP Client 追踪
                if (config.InstrumentHttpClient)
                {
                    tracing.AddHttpClientInstrumentation();
                }

                // EF Core 追踪。SQL 文本是否随 span 出进程由 RecordDbStatementText 决定（默认不出）。
                if (config.InstrumentEntityFramework)
                {
                    tracing.AddEntityFrameworkCoreInstrumentation(options => ApplyEntityFrameworkOptions(options, config));
                }

                // 配置导出器
                ConfigureOtlpExporter(tracing, config);
            });
        }

        // 配置 Metrics
        if (config.EnableMetrics)
        {
            otelBuilder.WithMetrics(metrics =>
            {
                // Subscribe to framework and custom Meters
                if (meterNames.Length > 0)
                {
                    metrics.AddMeter(meterNames);
                }

                // ASP.NET Core 指标
                if (config.InstrumentAspNetCore)
                {
                    metrics.AddAspNetCoreInstrumentation();
                }

                // HTTP Client 指标
                if (config.InstrumentHttpClient)
                {
                    metrics.AddHttpClientInstrumentation();
                }

                // 运行时指标
                metrics.AddRuntimeInstrumentation();

                // 配置导出器
                ConfigureOtlpExporter(metrics, config);
            });
        }

        // 配置 Logging（可选，资源信息通过 otelBuilder.ConfigureResource 统一继承）
        if (config.EnableLogging)
        {
            context.Services.AddLogging(logging =>
            {
                logging.AddOpenTelemetry(otelLogging =>
                {
                    ConfigureOtlpExporter(otelLogging, config);
                });
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Collect ActivitySource names from framework well-known sources and additional configuration
    /// </summary>
    public static string[] CollectActivitySourceNames(OpenTelemetryOptions config)
    {
        var sources = new List<string>();

        // Add framework sources (Tnzi.AI, Tnzi.AI.Rag, Tnzi.Framework)
        if (config.InstrumentFramework)
        {
            sources.AddRange(FrameworkActivitySourceNames);
        }

        // Add user-configured additional sources
        if (config.AdditionalActivitySources.Count > 0)
        {
            sources.AddRange(config.AdditionalActivitySources.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        return sources.Distinct().ToArray();
    }

    /// <summary>
    /// Collect Meter names from framework well-known meters and additional configuration
    /// </summary>
    public static string[] CollectMeterNames(OpenTelemetryOptions config)
    {
        var meters = new List<string>();

        // Add framework meters (Tnzi.AI, Tnzi.AI.Rag, Tnzi.Framework)
        if (config.InstrumentFramework)
        {
            meters.AddRange(FrameworkMeterNames);
        }

        // Add user-configured additional meters
        if (config.AdditionalMeters.Count > 0)
        {
            meters.AddRange(config.AdditionalMeters.Where(m => !string.IsNullOrWhiteSpace(m)));
        }

        return meters.Distinct().ToArray();
    }

    /// <summary>
    /// 配置 OTLP 导出器（Tracing）
    /// </summary>
    private static void ConfigureOtlpExporter(TracerProviderBuilder builder, OpenTelemetryOptions config)
    {
        if (!string.IsNullOrEmpty(config.OtlpEndpoint))
        {
            builder.AddOtlpExporter(otlp => ApplyOtlpOptions(otlp, config, OtlpSignal.Traces));
        }

        if (config.ExportToConsole)
        {
            builder.AddConsoleExporter();
        }
    }

    /// <summary>
    /// 配置 OTLP 导出器（Metrics）
    /// </summary>
    private static void ConfigureOtlpExporter(MeterProviderBuilder builder, OpenTelemetryOptions config)
    {
        if (!string.IsNullOrEmpty(config.OtlpEndpoint))
        {
            builder.AddOtlpExporter(otlp => ApplyOtlpOptions(otlp, config, OtlpSignal.Metrics));
        }

        if (config.ExportToConsole)
        {
            builder.AddConsoleExporter();
        }
    }

    /// <summary>
    /// 配置 OTLP 导出器（Logging）
    /// </summary>
    private static void ConfigureOtlpExporter(OpenTelemetryLoggerOptions builder, OpenTelemetryOptions config)
    {
        if (!string.IsNullOrEmpty(config.OtlpEndpoint))
        {
            builder.AddOtlpExporter(otlp => ApplyOtlpOptions(otlp, config, OtlpSignal.Logs));
        }

        if (config.ExportToConsole)
        {
            builder.AddConsoleExporter();
        }
    }

    /// <summary>
    /// 遥测有没有地方可去：OTLP 端点或控制台导出器至少配了一个。
    /// </summary>
    /// <remarks>
    /// ★ 两个都没配时三处 <c>ConfigureOtlpExporter</c> 一个导出器都不加：全量采样、全部检测开着，
    /// 每个 span 与指标被静默丢弃。<see cref="OpenTelemetryOptionsValidator"/> 据此在启动期告警。
    /// </remarks>
    public static bool HasExportTarget(OpenTelemetryOptions config)
    {
        Check.NotNull(config);
        return !string.IsNullOrWhiteSpace(config.OtlpEndpoint) || config.ExportToConsole;
    }

    /// <summary>
    /// 把 <see cref="OpenTelemetryOptions.OtlpHeaders"/> 拼成导出器要的 <c>k1=v1,k2=v2</c>；没有头时返回 null。
    /// </summary>
    /// <remarks>
    /// 值里的逗号与键里的等号无法转义，由验证器在启动期拒绝；这里只负责拼接。
    /// </remarks>
    public static string? ComposeOtlpHeaders(IReadOnlyDictionary<string, string> headers)
    {
        Check.NotNull(headers);
        if (headers.Count == 0)
        {
            return null;
        }

        return string.Join(',', headers.Select(h => $"{h.Key}={h.Value}"));
    }

    /// <summary>
    /// ASP.NET Core 服务端 span 的检测选项：不追踪 health check 与 swagger 请求，
    /// <c>url.path</c> 里作为凭据的路由参数记为掩码。
    /// </summary>
    /// <remarks>
    /// 框架自己的路由把凭据放在路径里（<c>signing/{token}</c>、<c>files/share/{token}</c>），
    /// 而 <c>url.path</c> 随每个服务端 span 导出到采集器。
    /// 检测库在请求开始时写入它，那时还没有路由值；响应回调在管线跑完之后调用，按路由值重写这一项，
    /// 名单与请求日志、审计、访问日志同源（<see cref="QueryStringRedactor.DefaultSensitiveKeys"/>）。
    /// </remarks>
    internal static void ConfigureAspNetCoreTracing(AspNetCoreTraceInstrumentationOptions options)
    {
        options.Filter = ctx =>
            !ctx.Request.Path.StartsWithSegments("/health") &&
            !ctx.Request.Path.StartsWithSegments("/swagger");
        options.EnrichWithHttpResponse = (activity, response) =>
        {
            if (activity.GetTagItem(UrlPathTag) is not string path)
            {
                return;
            }

            var redacted = RequestPathRedactor.Redact(path, response.HttpContext.Request.RouteValues, QueryStringRedactor.DefaultSensitiveKeys);
            if (!ReferenceEquals(redacted, path))
            {
                activity.SetTag(UrlPathTag, redacted);
            }
        };
    }

    /// <summary>OpenTelemetry 语义约定里服务端 span 的请求路径属性。</summary>
    private const string UrlPathTag = "url.path";

    /// <summary>
    /// EF Core 检测选项：SQL 文本默认<b>不</b>随 span 出进程。
    /// </summary>
    /// <remarks>
    /// 检测库没有关掉 SQL 文本的开关（<c>SetDbStatementForText</c> 已被删除），每条文本命令都会写进
    /// <c>db.query.text</c>（新约定）或 <c>db.statement</c>（旧约定）：认得出的 SQL 方言把字面量脱敏，
    /// 认不出的方言原样写。<see cref="ApplyDbStatementPolicy"/> 在它之后按 <see cref="OpenTelemetryOptions.RecordDbStatementText"/> 收尾。
    /// </remarks>
    internal static void ApplyEntityFrameworkOptions(EntityFrameworkInstrumentationOptions options, OpenTelemetryOptions config)
    {
        var record = config.RecordDbStatementText;
        // 选项上只有一个回调：先跑已经配上的那个（例如应用自己加的标签），再收尾，策略总是最后说了算
        var configured = options.EnrichWithIDbCommand;
        options.EnrichWithIDbCommand = (activity, command) =>
        {
            configured?.Invoke(activity, command);
            ApplyDbStatementPolicy(activity, command, record);
        };
    }

    /// <summary>新旧两种语义约定下承载 SQL 文本的属性名。</summary>
    internal static readonly string[] DbStatementTagNames = ["db.query.text", "db.statement"];

    /// <summary>
    /// 关：两种约定下的 SQL 文本属性都摘掉。开：写命令原文。这个开关是给排障用的，要的是能照着复现的那条 SQL，
    /// 而不是字面量被替换成 <c>?</c> 的脱敏版本。只管文本命令：存储过程命令的这两个属性写的是过程名，
    /// 不带数据，照旧保留。
    /// </summary>
    internal static void ApplyDbStatementPolicy(Activity activity, IDbCommand command, bool record)
    {
        if (command.CommandType != CommandType.Text)
        {
            return;
        }

        foreach (var tag in DbStatementTagNames)
        {
            if (activity.GetTagItem(tag) is null)
            {
                continue;
            }

            activity.SetTag(tag, record ? command.CommandText : null);
        }
    }

    /// <summary>
    /// 应用 OTLP 导出器公共配置：端点（按信号）、协议、认证头、超时。
    /// </summary>
    internal static void ApplyOtlpOptions(OtlpExporterOptions otlp, OpenTelemetryOptions config, OtlpSignal signal)
    {
        otlp.Endpoint = ResolveOtlpEndpoint(config.OtlpEndpoint!, config.UseGrpc, signal);
        otlp.Protocol = config.UseGrpc
            ? OtlpExportProtocol.Grpc
            : OtlpExportProtocol.HttpProtobuf;
        otlp.TimeoutMilliseconds = config.OtlpTimeoutMilliseconds;

        // 托管采集器（Authorization / x-api-key）的认证靠它；没有配头时保留导出器自己的默认值
        //（它还会读 OTEL_EXPORTER_OTLP_HEADERS 环境变量），不要用 null 把那条路盖掉。
        var headers = ComposeOtlpHeaders(config.OtlpHeaders);
        if (headers != null)
        {
            otlp.Headers = headers;
        }
    }

    /// <summary>
    /// 一个信号实际投递的 OTLP 地址。gRPC 原样使用配置的端点；HTTP/protobuf 以它为基地址追加 <c>/v1/{signal}</c>。
    /// </summary>
    /// <remarks>
    /// ★ 导出器只在端点<b>没有</b>被显式设置时才替 HttpProtobuf 追加信号路径，而这里总是显式设置：此前填 OTLP/HTTP 的
    /// 标准基地址（<c>http://collector:4318</c>）时三个信号都 POST 到根路径、被采集器拒绝，错误只进 OTel 内部的
    /// EventSource；填 <c>.../v1/traces</c> 则指标与日志被投到 traces 的路径，HTTP 下没有一个值能让三个信号都到达。
    /// 基地址可以带路径前缀（<c>https://host/otlp</c> 得到 <c>/otlp/v1/traces</c>）；已经以某个信号路径结尾的，先剥掉再按本信号追加，
    /// 所以此前为 traces 填好的完整地址升级后三个信号都能到。
    /// </remarks>
    internal static Uri ResolveOtlpEndpoint(string endpoint, bool useGrpc, OtlpSignal signal)
    {
        var uri = new Uri(endpoint);
        if (useGrpc)
        {
            return uri;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        foreach (var known in Enum.GetValues<OtlpSignal>())
        {
            var suffix = "/v1/" + SignalPath(known);
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^suffix.Length];
                break;
            }
        }

        return new UriBuilder(uri) { Path = $"{path}/v1/{SignalPath(signal)}" }.Uri;
    }

    private static string SignalPath(OtlpSignal signal) => signal switch
    {
        OtlpSignal.Traces => "traces",
        OtlpSignal.Metrics => "metrics",
        OtlpSignal.Logs => "logs",
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null)
    };
}

/// <summary>OTLP 的三个信号，决定 HTTP/protobuf 下各自投递的路径。</summary>
internal enum OtlpSignal
{
    Traces,
    Metrics,
    Logs
}
