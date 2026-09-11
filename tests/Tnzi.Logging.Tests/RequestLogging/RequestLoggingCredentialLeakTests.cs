using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Tnzi.Modules;

namespace Tnzi.Logging.Tests.RequestLogging;

/// <summary>
/// 请求日志的取证测试：用 <see cref="LoggingModule"/> **自己**的级别配置建一个
/// 内存 sink 的 logger，把模块装进一条真实的 ASP.NET Core 管线，发一个查询串里
/// 带凭据的请求，然后检查落到 sink 的**每一条**事件。
///
/// 为什么要跑真管线而不是断言配置对象：这条链路上有两个我们不拥有的环节 ——
/// Serilog.AspNetCore 的 <c>RequestLoggingMiddleware</c>（<c>RequestPath</c> 取
/// <c>IHttpRequestFeature.Path</c> 还是含查询串的 <c>RawTarget</c>，由包的
/// <c>IncludeQueryInRequestPath</c> 默认值决定）和 ASP.NET Core 自己的
/// <c>Hosting.Diagnostics</c>（"Request starting/finished" 两行把 <c>QueryString</c>
/// 原文写成结构化属性）。断言我们的配置只能证明"我们没主动打开它"，
/// 证不了包的默认值是哪个 —— 而后者正是升级依赖时会无声改变的东西。
///
/// 三个参数名与 <c>RequestTrackingMiddleware</c> 的脱敏名单同源：
/// <c>access_token</c>（SignalR 传输携带的 JWT）、<c>sig</c>（文件签名令牌）、
/// <c>password</c>（分享链接口令）。
///
/// 全类共用静态 <see cref="Log.Logger"/>，xunit 对同一类内的测试是串行的，
/// 类之间的并行由 <see cref="SerilogGlobalCollection"/> 挡住。
/// </summary>
[Collection(SerilogGlobalCollection.Name)]
public class RequestLoggingCredentialLeakTests
{
    private const string AccessTokenSecret = "eyJhbGciOiJIUzI1NiJ9.THIS_IS_THE_JWT";
    private const string SignatureSecret = "SIGNATURE-c0ffee-DO-NOT-LOG";
    private const string SharePasswordSecret = "share-passphrase-hunter2";

    private static readonly string SecretQuery =
        $"?access_token={AccessTokenSecret}&sig={SignatureSecret}&password={SharePasswordSecret}";

    /// <summary>
    /// 开箱即用的配置下，一次带凭据的请求不得在任何一条日志事件里留下明文。
    /// </summary>
    [Fact]
    public async Task DefaultConfiguration_WritesNoQueryStringCredential()
    {
        var sink = await CaptureAsync(new LoggingOptions(), "/files/report.pdf" + SecretQuery);

        AssertNoLeak(sink);
    }

    /// <summary>
    /// ★ B1 的回归测试：消费方配一条**与安全无关**的来源覆盖时，
    /// 框架默认的 <c>Microsoft.AspNetCore</c> → Warning 必须仍然生效。
    ///
    /// 修复前 <c>MinimumLevelOverrides</c> 是整份替换，这条配置会顺手删掉那道压级，
    /// 于是 Hosting 的两行诊断回到 Information，把 <c>QueryString</c> 原文写进
    /// <c>Logs/Information/log-*.txt</c> 与控制台 —— 而日志照常写、请求照常成功。
    /// </summary>
    [Fact]
    public async Task ConsumerSuppliedOverride_DoesNotReopenTheCredentialLeak()
    {
        var options = new LoggingOptions
        {
            MinimumLevelOverrides = new Dictionary<string, LogEventLevel>
            {
                ["MyApp.Data"] = LogEventLevel.Debug,
            },
        };

        var sink = await CaptureAsync(options, "/files/report.pdf" + SecretQuery);

        AssertNoLeak(sink);
    }

    /// <summary>
    /// 抛异常的请求走另一条分支（Serilog 以 Error 记录并附异常），同样不得带出查询串。
    /// </summary>
    [Fact]
    public async Task ThrowingRequest_WritesNoQueryStringCredential()
    {
        var sink = await CaptureAsync(new LoggingOptions(), "/boom" + SecretQuery, throwOnRequest: true);

        AssertNoLeak(sink);
    }

    /// <summary>
    /// Serilog 自己那条请求完成事件记的是**不含查询串**的路径。
    ///
    /// ★ 这一条钉的是 Serilog.AspNetCore 的 <c>IncludeQueryInRequestPath</c> 默认值
    /// （当前为 false）。它翻过来的话，请求日志会从"路径"变成"完整原始目标"，
    /// 而这个变化在任何配置文件、任何 API 签名上都看不出来。
    /// </summary>
    [Fact]
    public async Task SerilogRequestCompletionEvent_RecordsPathWithoutQueryString()
    {
        var sink = await CaptureAsync(new LoggingOptions(), "/files/report.pdf" + SecretQuery);

        var completion = sink.RequestCompletionEvent();
        completion.ShouldNotBeNull();
        completion.Properties["RequestPath"].ToString().Trim('"').ShouldBe("/files/report.pdf");
    }

    /// <summary>
    /// 关掉请求日志时不得再有请求完成事件（否则"关掉"只是没写模板）。
    /// </summary>
    [Fact]
    public async Task DisablingRequestLogging_RemovesTheCompletionEvent()
    {
        var sink = await CaptureAsync(
            new LoggingOptions(),
            "/files/report.pdf" + SecretQuery,
            configuration: new Dictionary<string, string?> { ["Logging:RequestLogging:Enabled"] = "false" });

        sink.RequestCompletionEvent().ShouldBeNull();
    }

    /// <summary>
    /// 上面几条测的就是开箱即用的形态：请求日志默认开启、默认 Information 级。
    /// </summary>
    [Fact]
    public void RequestLogging_IsOnByDefault()
    {
        var options = new LoggingOptions();
        options.RequestLogging.Enabled.ShouldBeTrue();
        options.RequestLogging.Level.ShouldBe(LogEventLevel.Information);
    }

    private static void AssertNoLeak(InMemorySink sink)
    {
        foreach (var secret in new[] { AccessTokenSecret, SignatureSecret, SharePasswordSecret })
        {
            var offenders = sink.EventsContaining(secret);
            offenders.ShouldBeEmpty(
                $"credential leaked into {offenders.Count} log event(s):"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
        }
    }

    /// <summary>
    /// 用 <paramref name="options"/> 经模块自己的
    /// <see cref="LoggingModule.CreateBaseLoggerConfiguration"/> 建 logger（只换 sink），
    /// 装上模块的请求日志中间件，发一次请求，返回捕获到的全部事件。
    /// </summary>
    private static async Task<InMemorySink> CaptureAsync(
        LoggingOptions options,
        string target,
        bool throwOnRequest = false,
        Dictionary<string, string?>? configuration = null)
    {
        var sink = new InMemorySink();
        var previous = Log.Logger;
        Log.Logger = LoggingModule.CreateBaseLoggerConfiguration(options)
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
                        // 与真实启动完全一致：由模块自己装请求日志中间件。
                        new LoggingModule()
                            .OnApplicationInitializationAsync(
                                new ApplicationInitializationContext(app.ApplicationServices, app))
                            .GetAwaiter().GetResult();

                        app.Run(_ => throwOnRequest
                            ? throw new InvalidOperationException("boom")
                            : Task.CompletedTask);
                    });
                })
                .StartAsync();

            try
            {
                await host.GetTestClient().GetAsync(target);
            }
            catch (InvalidOperationException)
            {
                // TestServer 把端点抛出的异常原样丢给客户端；
                // 此时请求完成事件已经写过了，正是要检查的东西。
            }

            await host.StopAsync();
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Logger = previous;
        }

        return sink;
    }
}
