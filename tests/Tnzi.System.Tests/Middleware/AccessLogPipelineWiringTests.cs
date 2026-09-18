using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AspNetCore.Extensions;
using Tnzi.AspNetCore.Middleware;
using Tnzi.Modules;
using Tnzi.Security.Claims;
using Tnzi.System.Middleware;

namespace Tnzi.System.Tests.Middleware;

/// <summary>
/// <see cref="SystemModule.ConfigureServicesAsync"/> 必须把 <see cref="AccessLogMiddleware"/> 登记到
/// <see cref="RequestPipelineStage.AfterAuthentication"/> 插入点，而且登记的动作真的把它挂进管线。
/// </summary>
/// <remarks>
/// <para>
/// 中间件本身的单测（<see cref="AccessLogMiddlewareTests"/>）看不见接线：把登记那一行删掉，
/// 它们照样全绿，而 <c>Sys_AccessLog</c> 回到恒空。所以这里跑真实的模块服务注册，把登记取出来
/// 应用到 <c>ApplicationBuilder</c> 上 <c>Build()</c> 出管线再发一次请求。
/// </para>
/// <para>
/// ★ 阶段必须是 <c>AfterAuthentication</c>：曾在 <c>OnApplicationInitializationAsync</c> 里追加在授权之后，
/// 那里看不见 401 / 403 / 429（被拒绝的请求由授权与限流中间件就地写出、不再调用下游）。
/// 「插入点真的在限流与授权之前」由 AspNetCore 测试项目里的端到端用例守着，这里只守 System 这一侧的登记。
/// </para>
/// </remarks>
public class AccessLogPipelineWiringTests
{
    private sealed class CapturingSender : IAccessLogSender
    {
        public List<AccessLogDto> Captured { get; } = [];

        public Task SendAsync(AccessLogDto log)
        {
            Captured.Add(log);
            return Task.CompletedTask;
        }
    }

    private sealed class StaticOptionsMonitor(AccessLogOptions value) : IOptionsMonitor<AccessLogOptions>
    {
        public AccessLogOptions CurrentValue => value;
        public AccessLogOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<AccessLogOptions, string?> listener) => null;
    }

    private static async Task<(ServiceProvider Provider, CapturingSender Sender)> ConfigureAsync(bool enabled)
    {
        var services = new ServiceCollection();
        await new SystemModule().ConfigureServicesAsync(new ServiceConfigurationContext(services, new ConfigurationBuilder().Build()));

        // 模块注册的发送者与选项换成测试替身；登记本身（RequestPipelineRegistration）原样保留。
        var sender = new CapturingSender();
        services.RemoveAll<IAccessLogSender>();
        services.AddSingleton<IAccessLogSender>(sender);
        services.RemoveAll<IOptionsMonitor<AccessLogOptions>>();
        services.AddSingleton<IOptionsMonitor<AccessLogOptions>>(new StaticOptionsMonitor(new AccessLogOptions { Enabled = enabled }));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped(_ => new Mock<ICurrentUser>().Object);

        return (services.BuildServiceProvider(), sender);
    }

    private static async Task<CapturingSender> RunPipelineAsync(bool enabled)
    {
        var (provider, sender) = await ConfigureAsync(enabled);

        var app = new ApplicationBuilder(provider);
        app.UseRequestPipelineStage(RequestPipelineStage.AfterAuthentication);
        var pipeline = app.Build();

        using var scope = provider.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = "GET";
        context.Request.Path = "/api/orders";
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        await pipeline(context);

        return sender;
    }

    [Fact]
    public async Task SystemModule_RegistersTheAccessLogMiddlewareAfterAuthentication()
    {
        var (provider, _) = await ConfigureAsync(enabled: true);

        var registration = provider.GetServices<RequestPipelineRegistration>().ShouldHaveSingleItem();
        registration.Stage.ShouldBe(RequestPipelineStage.AfterAuthentication);
    }

    [Fact]
    public async Task SystemModule_PutsTheAccessLogMiddlewareInThePipeline()
    {
        var sender = await RunPipelineAsync(enabled: true);

        var log = sender.Captured.ShouldHaveSingleItem();
        log.Path.ShouldBe("/api/orders");
        log.Method.ShouldBe("GET");
        log.StatusCode.ShouldBe(404, "an empty pipeline ends in 404; the point is that the request went through the middleware");
    }

    /// <summary>对照：中间件在管线里，但开关关着时一条都不记。</summary>
    [Fact]
    public async Task SystemModule_MiddlewareStaysQuietWhenDisabled()
    {
        var sender = await RunPipelineAsync(enabled: false);

        sender.Captured.ShouldBeEmpty();
    }

    /// <summary>在模块自己的初始化里不再追加：那会挂在授权之后，看不见被拒绝的请求。</summary>
    [Fact]
    public async Task SystemModule_DoesNotAppendTheMiddlewareAtInitialization()
    {
        var (provider, sender) = await ConfigureAsync(enabled: true);

        var app = new ApplicationBuilder(provider);
        await new SystemModule().OnApplicationInitializationAsync(new ApplicationInitializationContext(provider, app));
        var pipeline = app.Build();

        using var scope = provider.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Path = "/api/orders";
        await pipeline(context);

        sender.Captured.ShouldBeEmpty();
    }
}
