using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Serilog;
using Tnzi.Logging;
using Tnzi.Modules;

namespace Tnzi.OpenTelemetry.Tests;

/// <summary>
/// <c>OpenTelemetry:EnableLogging</c> 真的能把一条日志送到导出器。
/// </summary>
/// <remarks>
/// <para>
/// 日志导出是以 Microsoft.Extensions.Logging 的 <c>ILoggerProvider</c> 交付的
/// （<c>logging.AddOpenTelemetry(...)</c>），而 <see cref="LoggingModule"/> 用 Serilog
/// 接管了 <c>ILoggerFactory</c>。两个模块结构上总是一起加载（OpenTelemetry 依赖 AspNetCore，
/// AspNetCore 依赖 Logging），此前 Logging 调的是不带 <c>providers</c> 的 <c>AddSerilog()</c>，
/// 于是 <c>OpenTelemetryLoggerProvider</c> 注册了却从没被问过：追踪与指标到了采集器，
/// 日志管线永远是零条，而启动成功、验证器也不告警（端点是配了的）。
/// </para>
/// <para>
/// 此前本项目只测选项与验证器，从没构建过容器，看不见这一类缺陷。
/// 这里按真实加载顺序跑两个模块的 Configure，把导出器换成内存里的一个，记一条日志，数导出条数。
/// </para>
/// </remarks>
public class LogExportBridgeTests : IDisposable
{
    private readonly Serilog.ILogger _previousLogger = Log.Logger;

    public void Dispose()
    {
        Log.CloseAndFlush();
        Log.Logger = _previousLogger;
    }

    [Fact]
    public async Task EnableLogging_ExportsRecordsThroughSerilogOwnedLoggerFactory()
    {
        var exporter = new CapturingLogExporter();
        await using var provider = await BuildAsync(exporter, enableLogging: true);

        provider.GetRequiredService<ILogger<LogExportBridgeTests>>()
            .LogInformation("otel-bridge-probe {Value}", 7);

        var record = exporter.Records.ShouldHaveSingleItem();
        record.CategoryName.ShouldBe(typeof(LogExportBridgeTests).FullName);
        record.LogLevel.ShouldBe(LogLevel.Information);
    }

    [Fact]
    public async Task EnableLoggingOff_ExportsNothing()
    {
        // 防锈：开关关着时 provider 根本不注册，导出器一条都不该收到。
        // 没有这一条，「桥接生效」与「导出器在别的路上被接上了」在上面那条里分不开。
        var exporter = new CapturingLogExporter();
        await using var provider = await BuildAsync(exporter, enableLogging: false);

        provider.GetRequiredService<ILogger<LogExportBridgeTests>>().LogInformation("silent");

        exporter.Records.ShouldBeEmpty();
    }

    /// <summary>
    /// 按真实顺序跑 Logging → OpenTelemetry 两个模块的 PreConfigure + Configure；
    /// 不配 OTLP 端点、不开控制台导出，导出器换成内存里的一个。
    /// </summary>
    private static async Task<ServiceProvider> BuildAsync(CapturingLogExporter exporter, bool enableLogging)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:EnableConsole"] = "false",
                ["Logging:EnableFile"] = "false",
                ["OpenTelemetry:EnableTracing"] = "false",
                ["OpenTelemetry:EnableMetrics"] = "false",
                ["OpenTelemetry:EnableLogging"] = enableLogging ? "true" : "false",
                ["OpenTelemetry:ExportToConsole"] = "false"
            })
            .Build();

        // 宿主本来就注册了 MEL 的骨架（ILogger<T> 等），这里补上，否则关掉导出时连 ILogger<T> 都解析不到。
        var services = new ServiceCollection().AddLogging();
        var context = new ServiceConfigurationContext(services, config);

        ITnziModule[] modules = [new LoggingModule(), new OpenTelemetryModule()];
        foreach (var module in modules)
        {
            await module.PreConfigureServicesAsync(context);
        }

        foreach (var module in modules)
        {
            await module.ConfigureServicesAsync(context);
        }

        // 与真实部署里的 OTLP 导出器同一个挂点（OpenTelemetryLoggerOptions），只是换成内存导出。
        services.Configure<OpenTelemetryLoggerOptions>(o =>
            o.AddProcessor(new SimpleLogRecordExportProcessor(exporter)));

        return services.BuildServiceProvider();
    }

    private sealed class CapturingLogExporter : BaseExporter<LogRecord>
    {
        private readonly List<LogRecord> _records = [];

        public IReadOnlyList<LogRecord> Records
        {
            get
            {
                lock (_records)
                {
                    return [.. _records];
                }
            }
        }

        public override ExportResult Export(in Batch<LogRecord> batch)
        {
            lock (_records)
            {
                foreach (var record in batch)
                {
                    _records.Add(record);
                }
            }

            return ExportResult.Success;
        }
    }
}
