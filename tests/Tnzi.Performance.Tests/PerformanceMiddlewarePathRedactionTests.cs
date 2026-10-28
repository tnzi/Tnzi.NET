using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tnzi.Performance.Middleware;
using Tnzi.Performance.Options;
using Tnzi.Performance.Services;

namespace Tnzi.Performance.Tests;

/// <summary>
/// 性能指标按请求记下路径，并经 <c>admin/performance</c> 的慢请求明细与按端点统计给持查看权限的人看。
/// 路径里作为凭据的路由参数（名单里的名字，如 <c>{token}</c>、<c>{tempToken}</c>）
/// 要与请求日志、审计、访问日志一样记为掩码。判定只看参数名，用例里的路径是合成的形状，不对应框架的某个端点。
/// </summary>
public class PerformanceMiddlewarePathRedactionTests
{
    private const string Secret = "PATH-TOKEN-5eC4e7-DO-NOT-RECORD";

    [Theory]
    [InlineData("/api/invitations/{0}", "token", "/api/invitations/***")]
    [InlineData("/api/auth/pending-actions/{0}/complete", "tempToken", "/api/auth/pending-actions/***/complete")]
    public async Task RouteCredentialInPath_IsMaskedInTheRecordedMetrics(string pathFormat, string routeKey, string expected)
    {
        var collector = new PerformanceCollector();
        var middleware = new PerformanceMiddleware(
            context =>
            {
                // 路由在下游匹配：路由值要到管线跑完之后才有，与真实请求同序。
                context.Request.RouteValues[routeKey] = Secret;
                return Task.CompletedTask;
            },
            NullLogger<PerformanceMiddleware>.Instance,
            collector,
            new StaticOptionsMonitor(new PerformanceOptions { SlowRequestThresholdMs = 0 }));

        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        context.Request.Method = "POST";
        context.Request.Path = pathFormat.Replace("{0}", Secret, StringComparison.Ordinal);

        await middleware.InvokeAsync(context);

        var recorded = collector.GetRecentRecords(10).ShouldHaveSingleItem();
        recorded.Path.ShouldBe(expected);
        collector.GetEndpointStats().ShouldAllBe(stats => !stats.Path.Contains(Secret));
    }

    [Fact]
    public async Task PathWithoutCredentialRouteValues_IsRecordedUnchanged()
    {
        var collector = new PerformanceCollector();
        var middleware = new PerformanceMiddleware(
            context =>
            {
                context.Request.RouteValues["id"] = "42";
                return Task.CompletedTask;
            },
            NullLogger<PerformanceMiddleware>.Instance,
            collector,
            new StaticOptionsMonitor(new PerformanceOptions()));

        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        context.Request.Method = "GET";
        context.Request.Path = "/api/orders/42";

        await middleware.InvokeAsync(context);

        collector.GetRecentRecords(10).ShouldHaveSingleItem().Path.ShouldBe("/api/orders/42");
    }

    private sealed class StaticOptionsMonitor(PerformanceOptions value) : IOptionsMonitor<PerformanceOptions>
    {
        public PerformanceOptions CurrentValue => value;

        public PerformanceOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<PerformanceOptions, string?> listener) => null;
    }
}
