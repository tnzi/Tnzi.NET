using static Tnzi.AspNetCore.Tests.Middleware.RequestTrackingTestSupport;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 请求日志的两条边：下游抛异常的请求也要留一条带状态码的记录；默认排除名单要覆盖探针与 Swagger 的子路径。
/// </summary>
public class RequestTrackingFailureAndExclusionTests
{
    [Fact]
    public async Task ARequestWhoseDownstreamThrows_IsStillLogged_WithTheStatusTheClientWillReceive()
    {
        // 异常处理中间件在追踪中间件外面；此前这里只有 try/finally，抛异常的请求一条请求日志都没有，
        // 客户端拿着 X-Request-Id 来问时服务端无从对上。
        var logger = new CapturingLogger();
        var failure = new ResourceNotFoundException("Order", 42);
        var middleware = NewMiddleware(_ => throw failure, logger, new RequestTrackingOptions());
        var context = NewContext();
        context.Request.Headers["X-Request-Id"] = "client-abc";

        var thrown = await Assert.ThrowsAsync<ResourceNotFoundException>(() => middleware.InvokeAsync(context));

        Assert.Same(failure, thrown);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("client-abc", entry.RequestId);
        Assert.Equal(404, entry.StatusCode);
    }

    [Fact]
    public async Task AnUnhandledSystemException_IsLoggedAs500()
    {
        var logger = new CapturingLogger();
        var middleware = NewMiddleware(_ => throw new InvalidOperationException("boom"), logger, new RequestTrackingOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(NewContext()));

        Assert.Equal(500, Assert.Single(logger.Entries).StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/api-docs/v1")]
    public async Task TheDefaultExclusions_CoverProbeAndSwaggerSubPaths(string path)
    {
        var logger = new CapturingLogger();
        var middleware = NewMiddleware(_ => Task.CompletedTask, logger, new RequestTrackingOptions());
        var context = NewContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData("/api/things")]
    [InlineData("/healthcare/records")]
    [InlineData("/swaggerish")]
    public async Task OrdinaryPaths_AreStillLogged(string path)
    {
        // 正控制：排除名单扩到子路径，但不能按前缀把恰好以同样字母开头的业务路径也吞掉。
        var logger = new CapturingLogger();
        var middleware = NewMiddleware(_ => Task.CompletedTask, logger, new RequestTrackingOptions());
        var context = NewContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        Assert.Single(logger.Entries);
    }
}
