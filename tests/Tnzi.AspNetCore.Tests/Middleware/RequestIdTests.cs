using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 请求标识来自调用方，而它被**写回响应头**。
///
/// 客户端带 `X-Request-Id` 是为了把自己的调用链和服务端日志对上，约定本身没问题；
/// 问题是此前既不限长度也不限字符 —— 带 `%0d%0a` 的值会让 Kestrel 在写头时抛异常
/// （任何人都能让任意一个请求变成 500），任意长的值则跟着每条日志与每个响应走。
/// </summary>
public class RequestIdTests
{
    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static async Task<HttpContext> RunAsync(Action<HttpContext> arrange)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection().BuildServiceProvider();
        context.Response.Body = new MemoryStream();
        arrange(context);

        var middleware = new RequestTrackingMiddleware(
            _ => Task.CompletedTask,
            NullLogger<RequestTrackingMiddleware>.Instance,
            new StaticMonitor<AspNetCoreOptions>(new AspNetCoreOptions()),
            new StaticMonitor<RequestTrackingOptions>(new RequestTrackingOptions()));

        await middleware.InvokeAsync(context);
        return context;
    }

    [Theory]
    [InlineData("abc\r\nX-Injected: yes")]
    [InlineData("abc\nSet-Cookie: session=stolen")]
    public async Task ARequestIdCarryingNewlines_IsReplaced(string forged)
    {
        var context = await RunAsync(ctx => ctx.Request.Headers["X-Request-Id"] = forged);

        var echoed = context.Response.Headers["X-Request-Id"].ToString();
        Assert.DoesNotContain("\r", echoed, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", echoed, StringComparison.Ordinal);
        Assert.NotEqual(forged, echoed);
        Assert.NotEmpty(echoed);
    }

    [Fact]
    public async Task AnOverlongRequestId_IsReplaced()
    {
        var overlong = new string('a', 4096);

        var context = await RunAsync(ctx => ctx.Request.Headers["X-Request-Id"] = overlong);

        Assert.NotEqual(overlong, context.Response.Headers["X-Request-Id"].ToString());
    }

    [Fact]
    public async Task AWellFormedRequestId_IsKept()
    {
        // 这才是这个头存在的理由：客户端的调用链要和服务端日志对得上。
        // 修复不能顺手把正常用法也一起砍掉。
        const string id = "7f3a1b2c-4d5e:span-01";

        var context = await RunAsync(ctx => ctx.Request.Headers["X-Request-Id"] = id);

        Assert.Equal(id, context.Response.Headers["X-Request-Id"].ToString());
        Assert.Equal(id, context.Items["RequestId"]);
    }

    [Fact]
    public async Task WithoutOne_TheServerGeneratesIt()
    {
        var context = await RunAsync(_ => { });

        Assert.Equal(32, context.Response.Headers["X-Request-Id"].ToString().Length);
    }

    [Fact]
    public async Task AMalformedIdFallsThroughToTheNextHeader()
    {
        // 三个头是一条优先级链，都由调用方给、都过同一道校验。
        // 头一个不能用时接着问下一个，而不是让整条请求没有标识。
        var context = await RunAsync(ctx =>
        {
            ctx.Request.Headers["X-Request-Id"] = "bad\r\nvalue";
            ctx.Request.Headers["X-Trace-Id"] = "trace-42";
        });

        Assert.Equal("trace-42", context.Response.Headers["X-Request-Id"].ToString());
    }

    [Theory]
    // 常见的追踪标识格式都要能通过 —— 修复不能顺手把这个头的正常用法一起砍掉。
    [InlineData("3f2504e04f8911d39a0c0305e82c3301", true)]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301", true)]
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01", true)]
    [InlineData("checkout-api:span.7", true)]
    [InlineData("has space", false)]
    [InlineData("has\ttab", false)]
    public async Task TheAcceptedShapes(string candidate, bool accepted)
    {
        var context = await RunAsync(ctx => ctx.Request.Headers["X-Request-Id"] = candidate);

        var echoed = context.Response.Headers["X-Request-Id"].ToString();
        Assert.Equal(accepted, echoed == candidate);
    }
}
