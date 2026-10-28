using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tnzi.AspNetCore.Extensions;
using Tnzi.AspNetCore.Middleware;
using Tnzi.Exceptions;
using Tnzi.Modules;
using Tnzi.Performance.Middleware;
using Tnzi.Performance.Options;
using Tnzi.Performance.Services;

namespace Tnzi.Performance.Tests;

/// <summary>
/// <see cref="PerformanceMiddleware"/>：样本里有什么、按什么分组，以及它挂在管线的哪里。
/// </summary>
public class PerformanceMiddlewareTests
{
    private sealed class StaticOptionsMonitor(PerformanceOptions value) : IOptionsMonitor<PerformanceOptions>
    {
        public PerformanceOptions CurrentValue => value;
        public PerformanceOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<PerformanceOptions, string?> listener) => null;
    }

    private sealed class CapturingLogger : ILogger<PerformanceMiddleware>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);
    }

    private sealed class WebSocketRequestFeature : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;
        public Task<System.Net.WebSockets.WebSocket> AcceptAsync(WebSocketAcceptContext context) => throw new NotSupportedException();
    }

    private static (PerformanceMiddleware Middleware, PerformanceCollector Collector, CapturingLogger Logger) Build(
        RequestDelegate next, PerformanceOptions? options = null)
    {
        var collector = new PerformanceCollector();
        var logger = new CapturingLogger();
        var middleware = new PerformanceMiddleware(next, logger, collector, new StaticOptionsMonitor(options ?? new PerformanceOptions()));
        return (middleware, collector, logger);
    }

    private static DefaultHttpContext Request(string path = "/api/users/42", string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        return context;
    }

    private static RouteEndpoint RouteEndpointFor(string template, params object[] metadata)
        => new(_ => Task.CompletedTask, RoutePatternFactory.Parse(template), 0, new EndpointMetadataCollection(metadata), template);

    // ---------- 抛异常的请求 ----------

    /// <summary>
    /// ★ 回归：抛异常的请求此前不进样本（记录在 <c>await _next</c> 之后），故障期间错误数与 P95 反而更好看；
    /// 还为每个异常多记一条归到性能中间件名下的 Error。
    /// </summary>
    [Fact]
    public async Task DownstreamBusinessException_IsSampledWithItsStatusCode_AndNotLoggedAgain()
    {
        var (middleware, collector, logger) = Build(_ => throw new ResourceNotFoundException("Order", 42));

        await Should.ThrowAsync<ResourceNotFoundException>(() => middleware.InvokeAsync(Request()));

        collector.GetRecentRecords(10).ShouldHaveSingleItem().StatusCode.ShouldBe(404);
        logger.Levels.ShouldNotContain(LogLevel.Error);
    }

    [Fact]
    public async Task DownstreamUnhandledException_IsSampledAs500()
    {
        var (middleware, collector, _) = Build(_ => throw new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(() => middleware.InvokeAsync(Request()));

        collector.GetRecentRecords(10).ShouldHaveSingleItem().StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task ClientAbort_IsSampledAs499()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var context = Request();
        context.RequestAborted = aborted.Token;
        var (middleware, collector, _) = Build(_ => throw new OperationCanceledException(aborted.Token));

        await Should.ThrowAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));

        collector.GetRecentRecords(10).ShouldHaveSingleItem().StatusCode.ShouldBe(StatusCodes.Status499ClientClosedRequest);
    }

    // ---------- 长连接 ----------

    [Fact]
    public async Task WebSocketRequest_IsNotSampled()
    {
        var context = Request("/ws/gateway");
        context.Features.Set<IHttpWebSocketFeature>(new WebSocketRequestFeature());
        var (middleware, collector, logger) = Build(_ => Task.CompletedTask, new PerformanceOptions { SlowRequestThresholdMs = 0 });

        await middleware.InvokeAsync(context);

        collector.GetRecentRecords(10).ShouldBeEmpty();
        logger.Levels.ShouldBeEmpty();
    }

    /// <summary>Hub 的连接端点覆盖 SSE 与长轮询传输，不只是 WebSocket。</summary>
    [Fact]
    public async Task HubConnection_IsNotSampled()
    {
        var context = Request("/hubs/chat");
        context.SetEndpoint(RouteEndpointFor("/hubs/chat", new HubMetadata(typeof(Hub))));
        var (middleware, collector, _) = Build(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        collector.GetRecentRecords(10).ShouldBeEmpty();
    }

    /// <summary>negotiate 是普通的短请求：照常采样。</summary>
    [Fact]
    public async Task HubNegotiate_IsSampled()
    {
        var context = Request("/hubs/chat/negotiate", "POST");
        context.SetEndpoint(RouteEndpointFor("/hubs/chat/negotiate", new HubMetadata(typeof(Hub)), new NegotiateMetadata()));
        var (middleware, collector, _) = Build(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        collector.GetRecentRecords(10).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task EventStreamResponse_IsNotSampled_NorReportedAsSlow()
    {
        var (middleware, collector, logger) = Build(
            ctx =>
            {
                ctx.Response.ContentType = "text/event-stream";
                return Task.CompletedTask;
            },
            new PerformanceOptions { SlowRequestThresholdMs = 0 });

        await middleware.InvokeAsync(Request("/api/ai/chat/stream", "POST"));

        collector.GetRecentRecords(10).ShouldBeEmpty();
        logger.Levels.ShouldBeEmpty();
    }

    /// <summary>对照：普通请求超过阈值照样告警。</summary>
    [Fact]
    public async Task OrdinarySlowRequest_IsSampledAndReported()
    {
        var (middleware, collector, logger) = Build(_ => Task.CompletedTask, new PerformanceOptions { SlowRequestThresholdMs = 0 });

        await middleware.InvokeAsync(Request());

        collector.GetRecentRecords(10).ShouldHaveSingleItem();
        logger.Levels.ShouldBe([LogLevel.Warning]);
    }

    // ---------- 路由模板 ----------

    [Fact]
    public async Task Sample_CarriesTheRouteTemplate_AndKeepsTheRawPath()
    {
        var context = Request("/api/users/42");
        context.SetEndpoint(RouteEndpointFor("api/users/{id}"));
        var (middleware, collector, _) = Build(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var sample = collector.GetRecentRecords(10).ShouldHaveSingleItem();
        sample.Route.ShouldBe("/api/users/{id}");
        sample.Path.ShouldBe("/api/users/42");
    }

    /// <summary>
    /// 约定路由（<c>{controller=Home}/{action=Index}/{id?}</c>）的每个 action 是一个独立端点，却共用同一段原始模板。
    /// 按原始模板分组会把所有约定路由的 action 并成一行；必需值（controller / action）要代回模板，与限流键同一口径。
    /// </summary>
    [Fact]
    public async Task ConventionalRouteActions_AreSeparateEndpoints()
    {
        var (middleware, collector, _) = Build(_ => Task.CompletedTask);

        var users = Request("/Users/List/1");
        users.SetEndpoint(ConventionalEndpoint("Users", "List"));
        var orders = Request("/Orders/List/1");
        orders.SetEndpoint(ConventionalEndpoint("Orders", "List"));

        await middleware.InvokeAsync(users);
        await middleware.InvokeAsync(orders);

        collector.GetRecentRecords(10).Select(r => r.Route).OrderBy(r => r)
            .ShouldBe(["/orders/list/{id}", "/users/list/{id}"]);
        collector.GetEndpointStats().Count.ShouldBe(2);
    }

    private static RouteEndpoint ConventionalEndpoint(string controller, string action)
    {
        const string template = "{controller=Home}/{action=Index}/{id?}";
        var pattern = RoutePatternFactory.Parse(template, defaults: null, parameterPolicies: null,
            requiredValues: new { controller, action });
        return new RouteEndpoint(_ => Task.CompletedTask, pattern, 0, EndpointMetadataCollection.Empty, $"{controller}.{action}");
    }

    /// <summary>没有匹配端点：共用一个键，扫描器造不出成千上万个「端点」。</summary>
    [Fact]
    public async Task UnmatchedRequest_SharesOneRouteKey()
    {
        var (middleware, collector, _) = Build(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(Request("/wp-login.php"));
        await middleware.InvokeAsync(Request("/.env"));

        collector.GetRecentRecords(10).ShouldAllBe(r => r.Route == PerformanceMiddleware.UnmatchedRoute);
        collector.GetEndpointStats().ShouldHaveSingleItem().RequestCount.ShouldBe(2);
    }

    // ---------- 管线位置 ----------

    /// <summary>
    /// ★ 登记在「认证之后、限流与授权之前」：追加在授权之后看不见 401 / 403 / 429。
    /// </summary>
    [Fact]
    public async Task Module_RegistersTheMiddlewareAfterAuthentication()
    {
        var services = await ConfigureModuleAsync();

        var registration = services.BuildServiceProvider()
            .GetServices<RequestPipelineRegistration>().ShouldHaveSingleItem();
        registration.Stage.ShouldBe(RequestPipelineStage.AfterAuthentication);
    }

    [Fact]
    public async Task Module_PutsTheMiddlewareInThePipeline()
    {
        var services = await ConfigureModuleAsync();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IOptionsMonitor<PerformanceOptions>>(new StaticOptionsMonitor(new PerformanceOptions()));
        using var provider = services.BuildServiceProvider();

        var app = new ApplicationBuilder(provider);
        app.UseRequestPipelineStage(RequestPipelineStage.AfterAuthentication);
        var pipeline = app.Build();

        var context = Request("/api/orders");
        context.RequestServices = provider;
        await pipeline(context);

        provider.GetRequiredService<IPerformanceCollector>().GetRecentRecords(10).ShouldHaveSingleItem().Path.ShouldBe("/api/orders");
    }

    [Fact]
    public async Task Module_Disabled_RegistersNoMiddleware()
    {
        var services = await ConfigureModuleAsync(new Dictionary<string, string?> { ["Performance:Enabled"] = "false" });

        services.BuildServiceProvider().GetServices<RequestPipelineRegistration>().ShouldBeEmpty();
    }

    private static async Task<IServiceCollection> ConfigureModuleAsync(Dictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        await new PerformanceModule().ConfigureServicesAsync(new ServiceConfigurationContext(services, configuration));
        return services;
    }
}
