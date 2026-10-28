using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Tnzi.Modules;

namespace Tnzi.Logging.Tests.RequestLogging;

/// <summary>
/// 请求完成摘要行的级别。前三条跑真管线：模块自己装 Serilog 的请求日志中间件，
/// 端点决定状态码与耗时，断言落进 sink 的那条事件的级别。
/// </summary>
[Collection(SerilogGlobalCollection.Name)]
public class RequestLoggingLevelTests
{
    /// <summary>慢请求阈值 10ms：端点睡 50ms 即为慢请求。</summary>
    private static readonly Dictionary<string, string?> FastThreshold = new()
    {
        ["Logging:RequestLogging:SlowRequestThresholdSeconds"] = "0.01",
    };

    /// <summary>
    /// ★ 回归：5xx 不带异常也要记 Error。框架的异常处理中间件在请求日志中间件内层，
    /// 下游异常到这里已经是一个 500 响应、没有异常对象了。
    /// </summary>
    [Fact]
    public async Task ServerErrorStatusWithoutException_IsLoggedAsError()
    {
        var completion = await CaptureCompletionAsync(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        });

        completion.Level.ShouldBe(LogEventLevel.Error);
    }

    [Fact]
    public async Task SlowOrdinaryRequest_IsUpgradedToWarning()
    {
        var completion = await CaptureCompletionAsync(async _ => await Task.Delay(50), FastThreshold);

        completion.Level.ShouldBe(LogEventLevel.Warning);
    }

    /// <summary>
    /// SSE 响应的耗时是连接存活时长：超过阈值也保持配置的级别。
    /// </summary>
    [Fact]
    public async Task SlowEventStream_KeepsTheConfiguredLevel()
    {
        var completion = await CaptureCompletionAsync(async ctx =>
        {
            ctx.Response.ContentType = "text/event-stream";
            await ctx.Response.WriteAsync("data: x\n\n");
            await Task.Delay(50);
        }, FastThreshold);

        completion.Level.ShouldBe(LogEventLevel.Information);
    }

    [Fact]
    public void FastSuccessfulRequest_UsesTheConfiguredLevel()
    {
        var options = new RequestLoggingOptions { Level = LogEventLevel.Debug };

        LoggingModule.ResolveRequestLogLevel(new DefaultHttpContext(), 1, null, options)
            .ShouldBe(LogEventLevel.Debug);
    }

    [Fact]
    public void SlowHubConnection_KeepsTheConfiguredLevel()
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new HubMetadata(typeof(Hub))), "hub"));

        LoggingModule.ResolveRequestLogLevel(context, 60_000, null, new RequestLoggingOptions())
            .ShouldBe(LogEventLevel.Information);
    }

    /// <summary>
    /// negotiate 也挂着 HubMetadata，但它是一次普通的短请求：慢了照样升级。
    /// </summary>
    [Fact]
    public void SlowHubNegotiate_IsUpgradedToWarning()
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new HubMetadata(typeof(Hub)), new NegotiateMetadata()),
            "negotiate"));

        LoggingModule.ResolveRequestLogLevel(context, 60_000, null, new RequestLoggingOptions())
            .ShouldBe(LogEventLevel.Warning);
    }

    [Fact]
    public void ServerErrorOnALongLivedConnection_IsStillAnError()
    {
        var context = new DefaultHttpContext();
        context.Response.ContentType = "text/event-stream";
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

        LoggingModule.ResolveRequestLogLevel(context, 60_000, null, new RequestLoggingOptions())
            .ShouldBe(LogEventLevel.Error);
    }

    private static async Task<LogEvent> CaptureCompletionAsync(RequestDelegate endpoint, Dictionary<string, string?>? configuration = null)
    {
        var sink = new InMemorySink();
        var previous = Log.Logger;
        Log.Logger = LoggingModule.CreateBaseLoggerConfiguration(new LoggingOptions())
            .WriteTo.Sink(sink)
            .CreateLogger();

        try
        {
            using var host = await new HostBuilder()
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    if (configuration != null)
                    {
                        web.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(configuration));
                    }
                    web.ConfigureServices(services => services.AddSerilog());
                    web.Configure(app =>
                    {
                        new LoggingModule()
                            .OnApplicationInitializationAsync(
                                new ApplicationInitializationContext(app.ApplicationServices, app))
                            .GetAwaiter().GetResult();

                        app.Run(endpoint);
                    });
                })
                .StartAsync();

            await host.GetTestClient().GetAsync("/probe");
            await host.StopAsync();
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Logger = previous;
        }

        return sink.RequestCompletionEvent().ShouldNotBeNull();
    }
}
