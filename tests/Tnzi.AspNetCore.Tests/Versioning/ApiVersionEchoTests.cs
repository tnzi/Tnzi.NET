using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.AspNetCore.Tests.Versioning;

/// <summary>
/// 响应头里的 API 版本，来源是调用方，所以必须先校验再回写。
///
/// 守的是两条线：
/// ① 旧实现把调用方给的版本串**原样**写进响应头。带 `%0d%0a` 的版本号会让 Kestrel
///    在写头时抛异常 —— 于是任何人都能让任意一个请求变成 500，一行 curl 的事。
/// ② 没有受支持清单：一个不认识的版本被静默当成默认版本处理，
///    客户端以为自己在跟 v2 说话，服务端按 v1 回答，两边都不会报错。
/// </summary>
public class ApiVersionEchoTests
{
    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static async Task<(HttpContext Context, bool NextCalled)> RunAsync(
        string? requestedVersion,
        ApiVersionOptions? versionOptions = null)
    {
        var options = versionOptions ?? new ApiVersionOptions { Enabled = true };
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        if (requestedVersion != null)
        {
            context.Request.QueryString = new QueryString($"?{options.QueryStringName}={Uri.EscapeDataString(requestedVersion)}");
        }

        var nextCalled = false;
        var middleware = new ApiVersionMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            new StaticMonitor<AspNetCoreOptions>(new AspNetCoreOptions { ApiVersion = options }),
            NullLogger<ApiVersionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        return (context, nextCalled);
    }

    [Theory]
    [InlineData("2.0\r\nX-Injected: yes")]
    [InlineData("2.0\nSet-Cookie: session=stolen")]
    [InlineData("\r\n\r\n<html>")]
    public async Task AVersionCarryingNewlines_NeverReachesTheHeader(string forged)
    {
        // 这一条同时挡住两件事：头注入，以及 Kestrel 因非法头值抛异常导致的 500。
        var (context, nextCalled) = await RunAsync(forged);

        var echoed = context.Response.Headers["X-Api-Version"].ToString();
        Assert.DoesNotContain("\r", echoed, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", echoed, StringComparison.Ordinal);
        Assert.Equal("1.0", echoed);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AnOverlongVersion_IsNotEchoed()
    {
        // 无长度限制时，调用方能让每一条响应都背上一段任意长的字符串。
        var (context, _) = await RunAsync(new string('9', 5000));

        Assert.Equal("1.0", context.Response.Headers["X-Api-Version"].ToString());
    }

    [Fact]
    public async Task AMalformedVersion_FallsBackInsteadOfFailing()
    {
        // 刻意不 400：UrlPath 模式下版本是从路径里正则抠出来的，
        // 一条碰巧含有 /v/... 的普通路径会被误读成版本号。
        var (context, nextCalled) = await RunAsync("2.0 beta!");

        Assert.True(nextCalled);
        Assert.Equal("1.0", context.Items["ApiVersion"]);
    }

    [Fact]
    public async Task AWellFormedVersion_IsHonoured()
    {
        var (context, _) = await RunAsync("2.1");

        Assert.Equal("2.1", context.Items["ApiVersion"]);
        Assert.Equal("2.1", context.Response.Headers["X-Api-Version"].ToString());
    }

    [Fact]
    public async Task WithoutARequestedVersion_TheDefaultIsUsed()
    {
        var (context, _) = await RunAsync(requestedVersion: null);

        Assert.Equal("1.0", context.Items["ApiVersion"]);
    }

    [Fact]
    public async Task AnUnsupportedVersion_IsRejected()
    {
        var options = new ApiVersionOptions { Enabled = true, SupportedVersions = ["1.0", "2.0"] };

        var (context, nextCalled) = await RunAsync("3.0", options);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task ASupportedVersion_PassesThrough()
    {
        var options = new ApiVersionOptions { Enabled = true, SupportedVersions = ["1.0", "2.0"] };

        var (context, nextCalled) = await RunAsync("2.0", options);

        Assert.True(nextCalled);
        Assert.Equal("2.0", context.Items["ApiVersion"]);
    }

    [Fact]
    public async Task WithoutAList_AnyWellFormedVersionIsAccepted()
    {
        // 不配清单时保持宽松：版本只是被记进 Items 供下游取用，框架自己不按它分支。
        var (context, nextCalled) = await RunAsync("42.7");

        Assert.True(nextCalled);
        Assert.Equal("42.7", context.Items["ApiVersion"]);
    }

    [Fact]
    public async Task AMalformedDefaultVersion_IsNotEchoedEither()
    {
        // 配置本身也可能被写坏。响应头是一条出口，出口只认校验过的值。
        var options = new ApiVersionOptions { Enabled = true, DefaultVersion = "1.0\r\nX-Injected: yes" };

        var (context, _) = await RunAsync(requestedVersion: null, options);

        Assert.Equal(string.Empty, context.Response.Headers["X-Api-Version"].ToString());
    }
}
