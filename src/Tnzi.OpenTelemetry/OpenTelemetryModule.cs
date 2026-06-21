
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
                    tracing.AddAspNetCoreInstrumentation(options =>
                    {
                        // 过滤 health check 和 swagger 请求
                        options.Filter = ctx =>
                            !ctx.Request.Path.StartsWithSegments("/health") &&
                            !ctx.Request.Path.StartsWithSegments("/swagger");
                    });
                }

                // HTTP Client 追踪
                if (config.InstrumentHttpClient)
                {
                    tracing.AddHttpClientInstrumentation();
                }

                // EF Core 追踪
                if (config.InstrumentEntityFramework)
                {
                    tracing.AddEntityFrameworkCoreInstrumentation(options =>
                    {
                        options.SetDbStatementForText = true;
                    });
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
            builder.AddOtlpExporter(otlp => ApplyOtlpOptions(otlp, config));
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
            builder.AddOtlpExporter(otlp => ApplyOtlpOptions(otlp, config));
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
            builder.AddOtlpExporter(otlp => ApplyOtlpOptions(otlp, config));
        }

        if (config.ExportToConsole)
        {
            builder.AddConsoleExporter();
        }
    }

    /// <summary>
    /// 应用 OTLP 导出器公共配置
    /// </summary>
    private static void ApplyOtlpOptions(OtlpExporterOptions otlp, OpenTelemetryOptions config)
    {
        otlp.Endpoint = new Uri(config.OtlpEndpoint!);
        otlp.Protocol = config.UseGrpc
            ? OtlpExportProtocol.Grpc
            : OtlpExportProtocol.HttpProtobuf;
    }
}
