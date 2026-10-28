using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Tnzi.Logging;
using Tnzi.Modules;

namespace Tnzi.OpenTelemetry.Tests;

/// <summary>
/// OTLP/HTTP 下三个信号各自投到自己的路径：<c>/v1/traces</c>、<c>/v1/metrics</c>、<c>/v1/logs</c>。
/// </summary>
/// <remarks>
/// <para>
/// 导出器只在端点<b>没有</b>被显式设置时才替 HttpProtobuf 追加信号路径；框架一律显式设置端点，
/// 于是填 OTLP/HTTP 的标准基地址（<c>http://collector:4318</c>）时三个信号都 POST 到根路径，被采集器 404 丢弃，
/// 导出错误只进 OTel 内部的 EventSource，日志里一行都没有。改填 <c>.../v1/traces</c> 则 metrics 与 logs
/// 被投到 traces 的路径：HTTP 协议下这一个配置项无论填什么都不能让三个信号同时到达。
/// </para>
/// <para>
/// 这里起一个真的 HTTP 端点接导出请求，按真实加载顺序跑 Logging → OpenTelemetry 两个模块，
/// 看采集端实际收到的路径，而不是断言某个选项对象上的值。
/// </para>
/// </remarks>
[Collection(SerilogGlobalLoggerCollection.Name)]
public class OtlpHttpSignalPathTests : IDisposable
{
    private const string ProbeSourceName = "Tnzi.Tests.OtlpSignalPathProbe";

    private readonly Serilog.ILogger _previousLogger = Log.Logger;

    public void Dispose()
    {
        Log.CloseAndFlush();
        Log.Logger = _previousLogger;
    }

    [Theory]
    [InlineData("")]
    [InlineData("/otlp")]
    public async Task EachSignal_IsPostedToItsOwnPath_UnderTheConfiguredBase(string basePath)
    {
        var received = new ConcurrentBag<string>();
        await using var collector = await StartCollectorAsync(received);
        var endpoint = collector.Urls.First() + basePath;

        await EmitAllSignalsAsync(endpoint);

        received.ShouldContain($"{basePath}/v1/traces");
        received.ShouldContain($"{basePath}/v1/metrics");
        received.ShouldContain($"{basePath}/v1/logs");
        received.ShouldAllBe(path => path.StartsWith(basePath + "/v1/", StringComparison.Ordinal));
    }

    private static async Task EmitAllSignalsAsync(string endpoint)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:EnableConsole"] = "false",
                ["Logging:EnableFile"] = "false",
                ["OpenTelemetry:OtlpEndpoint"] = endpoint,
                ["OpenTelemetry:UseGrpc"] = "false",
                ["OpenTelemetry:EnableTracing"] = "true",
                ["OpenTelemetry:EnableMetrics"] = "true",
                ["OpenTelemetry:EnableLogging"] = "true",
                ["OpenTelemetry:InstrumentAspNetCore"] = "false",
                ["OpenTelemetry:InstrumentHttpClient"] = "false",
                ["OpenTelemetry:InstrumentEntityFramework"] = "false",
                ["OpenTelemetry:AdditionalActivitySources:0"] = ProbeSourceName,
                ["OpenTelemetry:AdditionalMeters:0"] = ProbeSourceName,
            })
            .Build();

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

        await using (var provider = services.BuildServiceProvider())
        {
            // 解析即构建。指标与日志显式 ForceFlush；追踪在容器释放、TracerProvider 关闭并清空批处理时导出。
            _ = provider.GetRequiredService<TracerProvider>();
            _ = provider.GetRequiredService<MeterProvider>();

            using (var source = new ActivitySource(ProbeSourceName))
            using (source.StartActivity("otlp-path-probe"))
            {
            }

            using (var meter = new Meter(ProbeSourceName))
            {
                meter.CreateCounter<long>("otlp_path_probe").Add(1);
                provider.GetRequiredService<MeterProvider>().ForceFlush();
            }

            provider.GetRequiredService<ILogger<OtlpHttpSignalPathTests>>().LogInformation("otlp-path-probe");
            provider.GetRequiredService<LoggerProvider>().ForceFlush();
        }
    }

    private static async Task<WebApplication> StartCollectorAsync(ConcurrentBag<string> received)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.Run(async http =>
        {
            received.Add(http.Request.Path.Value ?? string.Empty);
            await http.Request.Body.CopyToAsync(Stream.Null);
            http.Response.StatusCode = StatusCodes.Status200OK;
        });
        await app.StartAsync();
        return app;
    }
}
