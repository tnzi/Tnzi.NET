using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.SignalR;
using System.Net.WebSockets;
using static Tnzi.AspNetCore.Tests.Middleware.RequestTrackingTestSupport;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 请求日志那一条的级别：失败的请求不能跟着配置的低级别一起被过滤掉；长连接的存活时长不是处理时长。
/// </summary>
/// <remarks>
/// 这一条是 <c>X-Request-Id</c> 与连接级日志作用域之间唯一的桥：异常处理器的 Error 日志里没有客户端拿到的请求 ID。
/// 部署把 <see cref="RequestTrackingOptions.LogLevel"/> 调到 Debug 来压住正常流量时，5xx 那几条若也按 Debug 记，
/// 恰恰是最需要对上请求 ID 的请求从日志里消失。
/// </remarks>
public class RequestTrackingLogLevelTests
{
    [Fact]
    public async Task AServerErrorResponse_IsLoggedAtError_WhateverTheConfiguredLevel()
    {
        // 控制器以失败结果返回的 5xx：没有异常，只有状态码
        var logger = new CapturingLogger();
        var middleware = NewMiddleware(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Task.CompletedTask;
        }, logger, new RequestTrackingOptions { LogLevel = LogLevel.Debug });

        await middleware.InvokeAsync(NewContext());

        Assert.Equal(LogLevel.Error, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task ARequestWhoseDownstreamThrowsASystemException_IsLoggedAtError()
    {
        var logger = new CapturingLogger();
        var middleware = NewMiddleware(_ => throw new InvalidOperationException("boom"), logger,
            new RequestTrackingOptions { LogLevel = LogLevel.Information });

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(NewContext()));

        Assert.Equal(LogLevel.Error, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task AClientError_KeepsTheConfiguredLevel()
    {
        // 正控制：4xx 是调用方的问题，不升级
        var logger = new CapturingLogger();
        var middleware = NewMiddleware(_ => throw new ResourceNotFoundException("Order", 42), logger,
            new RequestTrackingOptions { LogLevel = LogLevel.Information });

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => middleware.InvokeAsync(NewContext()));

        Assert.Equal(LogLevel.Information, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task AnOrdinarySlowRequest_IsPromotedToWarning()
    {
        // 正控制：慢请求升级本身仍然生效
        var logger = new CapturingLogger();
        var middleware = NewMiddleware(async _ => await Task.Delay(20), logger,
            new RequestTrackingOptions { SlowRequestThresholdMs = 1 });

        await middleware.InvokeAsync(NewContext());

        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task AnEventStreamResponse_IsNotASlowRequest()
    {
        var logger = new CapturingLogger();
        var middleware = NewMiddleware(async ctx =>
        {
            ctx.Response.ContentType = "text/event-stream";
            await Task.Delay(20);
        }, logger, new RequestTrackingOptions { SlowRequestThresholdMs = 1 });

        await middleware.InvokeAsync(NewContext());

        Assert.Equal(LogLevel.Information, Assert.Single(logger.Levels));
        Assert.False(logger.Entries[0].IsSlow);
    }

    [Fact]
    public async Task AWebSocketRequest_IsNotASlowRequest()
    {
        var logger = new CapturingLogger();
        var middleware = NewMiddleware(async _ => await Task.Delay(20), logger,
            new RequestTrackingOptions { SlowRequestThresholdMs = 1 });
        var context = NewContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/ws/gateway";
        context.Features.Set<IHttpWebSocketFeature>(new WebSocketRequestFeature());

        await middleware.InvokeAsync(context);

        Assert.Equal(LogLevel.Information, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task AHubConnectionOnACustomPath_IsNotASlowRequest()
    {
        // 默认排除名单只挡 /hubs/*；挂在别的路径上的 Hub（SSE / 长轮询传输）同样是长连接
        var logger = new CapturingLogger();
        var middleware = NewMiddleware(async _ => await Task.Delay(20), logger,
            new RequestTrackingOptions { SlowRequestThresholdMs = 1 });
        var context = NewContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/realtime/notifications";
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("/realtime/notifications"), 0,
            new EndpointMetadataCollection(new HubMetadata(typeof(Hub))), "hub"));

        await middleware.InvokeAsync(context);

        Assert.Equal(LogLevel.Information, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task AHubNegotiateRequest_IsStillAnOrdinaryRequest()
    {
        // 正控制：negotiate 是一次普通的 POST，慢了照样是慢请求
        var logger = new CapturingLogger();
        var middleware = NewMiddleware(async _ => await Task.Delay(20), logger,
            new RequestTrackingOptions { SlowRequestThresholdMs = 1 });
        var context = NewContext();
        context.Request.Path = "/realtime/notifications/negotiate";
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("/realtime/notifications/negotiate"), 0,
            new EndpointMetadataCollection(new HubMetadata(typeof(Hub)), new NegotiateMetadata()), "negotiate"));

        await middleware.InvokeAsync(context);

        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Levels));
    }

    private sealed class WebSocketRequestFeature : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => throw new NotSupportedException();
    }
}
