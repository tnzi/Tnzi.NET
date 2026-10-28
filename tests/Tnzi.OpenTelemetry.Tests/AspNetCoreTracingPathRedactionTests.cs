using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Tnzi.OpenTelemetry.Tests;

/// <summary>
/// 服务端 span 的 <c>url.path</c> 随追踪导出到采集器，给看链路的人看。路径里作为凭据的路由参数
/// （名单里的名字，如 <c>{token}</c>、<c>{tempToken}</c>）要与请求日志、审计、访问日志一样记为掩码。
/// </summary>
/// <remarks>
/// <para>
/// 跑真实管线而不是断言配置对象：<c>url.path</c> 由检测库在请求开始时写入，那时还没有路由值；
/// 掩码能不能盖住它，取决于检测库调用响应回调的时机，只有真的发一次请求才看得出来。
/// </para>
/// <para>
/// 判定只看参数名，用例里的路由是本测试自己映射的形状，不对应框架的某个端点。
/// </para>
/// </remarks>
[Collection(AspNetCoreActivityCollection.Name)]
public class AspNetCoreTracingPathRedactionTests
{
    private const string Secret = "PATH-TOKEN-5eC4e7-DO-NOT-EXPORT";

    private sealed class CapturingProcessor : BaseProcessor<Activity>
    {
        private readonly List<Activity> _ended = [];

        public IReadOnlyList<Activity> Ended
        {
            get
            {
                lock (_ended)
                {
                    return [.. _ended];
                }
            }
        }

        public override void OnEnd(Activity data)
        {
            lock (_ended)
            {
                _ended.Add(data);
            }
        }
    }

    [Theory]
    [InlineData("/invitations/{token}", "/invitations/***")]
    [InlineData("/auth/pending-actions/{tempToken}/complete", "/auth/pending-actions/***/complete")]
    public async Task RouteCredentialInPath_IsMaskedOnTheServerSpan(string template, string expected)
    {
        var target = template
            .Replace("{token}", Secret, StringComparison.Ordinal)
            .Replace("{tempToken}", Secret, StringComparison.Ordinal);

        var span = await CaptureServerSpanAsync(template, target);

        span.GetTagItem("url.path").ShouldBe(expected);
        span.TagObjects
            .Where(tag => tag.Value?.ToString()?.Contains(Secret, StringComparison.Ordinal) == true)
            .Select(tag => tag.Key)
            .ShouldBeEmpty();
        span.DisplayName.ShouldNotContain(Secret);
    }

    [Fact]
    public async Task PathWithoutCredentialRouteValues_IsExportedUnchanged()
    {
        var span = await CaptureServerSpanAsync("/orders/{id}", "/orders/42");

        span.GetTagItem("url.path").ShouldBe("/orders/42");
    }

    private static async Task<Activity> CaptureServerSpanAsync(string template, string target)
    {
        var processor = new CapturingProcessor();
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddOpenTelemetry().WithTracing(tracing => tracing
                        .AddAspNetCoreInstrumentation(OpenTelemetryModule.ConfigureAspNetCoreTracing)
                        .AddProcessor(processor));
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGet(template, _ => Task.CompletedTask));
                });
            })
            .StartAsync();

        await host.GetTestClient().GetAsync(target);

        // 服务端 span 在请求管线收尾时结束，可能晚于客户端拿到响应。
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (processor.Ended.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        await host.StopAsync();
        return processor.Ended.ShouldHaveSingleItem();
    }
}
