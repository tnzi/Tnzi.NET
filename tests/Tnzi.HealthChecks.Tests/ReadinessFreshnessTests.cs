using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Tnzi.Modules;

namespace Tnzi.HealthChecks.Tests;

/// <summary>
/// 探针的回答必须是<b>这一次</b>检查的结果。
///
/// 此前的「响应缓存」挂在 <c>HealthCheckOptions.ResponseWriter</c> 上，而 ASP.NET Core 的
/// <c>HealthCheckMiddleware</c> 先跑完全部检查、按结果设好状态码、最后才调 ResponseWriter ——
/// 于是缓存命中时所有检查都已经跑过（一点工作都没省），刚算出来的 503 被改写成上一轮缓存的 200，
/// 编排器在最长 <c>CacheDurationSeconds</c>（默认 10 秒）内继续把流量送进一个依赖已经挂掉的实例；
/// 恢复之后同理再撑一个窗口的 503。它缓存的是「答案」不是「工作」，而就绪探针恰恰不能答旧答案。
/// </summary>
public class ReadinessFreshnessTests
{
    /// <summary>可以在运行中翻转结果的检查。</summary>
    private sealed class ToggleHealthCheck : IHealthCheck
    {
        public volatile bool Healthy = true;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(Healthy ? HealthCheckResult.Healthy("up") : HealthCheckResult.Unhealthy("down"));
    }

    [Theory]
    [InlineData("/health/ready")]
    [InlineData("/health")]
    public async Task Probe_ReflectsADependencyFailureImmediately(string path)
    {
        var toggle = new ToggleHealthCheck();
        await using var app = await BuildAsync(toggle);
        await app.StartAsync();

        var client = app.GetTestClient();

        using (var healthy = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
            Assert.Contains("\"Healthy\"", await healthy.Content.ReadAsStringAsync());
        }

        toggle.Healthy = false;

        // 仍在旧实现的缓存窗口（10 秒）之内：答案必须已经变成 503 / Unhealthy。
        using (var failed = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            var body = await failed.Content.ReadAsStringAsync();
            Assert.Contains("\"Unhealthy\"", body);
            Assert.DoesNotContain("\"status\": \"Healthy\"", body);
        }

        toggle.Healthy = true;

        // 恢复也要立刻反映：旧实现会在剩余窗口里继续答 503。
        using (var recovered = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        }
    }

    private static async Task<WebApplication> BuildAsync(IHealthCheck toggle)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();

        // 默认选项：DetailedOutput=true，只关掉需要真实依赖的四种内置检查，换成可翻转的那一个。
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HealthChecks:Enabled"] = "true",
            ["HealthChecks:EnableCacheCheck"] = "false",
            ["HealthChecks:EnableDatabaseCheck"] = "false",
            ["HealthChecks:EnableRedisCheck"] = "false",
            ["HealthChecks:EnableEventBusCheck"] = "false"
        });

        var module = new HealthChecksModule();
        var context = new ServiceConfigurationContext(builder.Services, builder.Configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);
        await module.PostConfigureServicesAsync(context);

        builder.Services.AddHealthChecks().AddCheck("toggle", toggle);

        var app = builder.Build();
        await module.OnApplicationInitializationAsync(new ApplicationInitializationContext(app.Services, app, app.Environment, app));
        return app;
    }
}
