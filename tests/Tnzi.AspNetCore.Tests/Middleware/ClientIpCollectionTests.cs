using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Tnzi.AspNetCore.Http;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 来源地址采集的部署级开关。
///
/// 守的是一条隐私性质的线：有些系统（匿名举报、举报人保护一类）要求
/// **日志里根本没有来源地址这个字段**，而不是「有但被打了星号」。
/// 后者是一行可以被误删的代码，前者在被要求交出日志时也无从交出。
///
/// 判定放在 GetClientIp 这个唯一的采集入口上，因此一处关闭，
/// 请求日志、访问日志、审计上下文与限流全部一次性拿不到地址。
/// </summary>
public class ClientIpCollectionTests
{
    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>造一个带真实来源地址的请求；options 为 null 表示容器里根本没有该选项。</summary>
    private static HttpContext ContextWith(AspNetCoreOptions? options, string ip = "203.0.113.7")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);

        var services = new ServiceCollection();
        if (options != null)
        {
            services.AddSingleton<IOptionsMonitor<AspNetCoreOptions>>(new StaticMonitor<AspNetCoreOptions>(options));
        }

        context.RequestServices = services.BuildServiceProvider();
        return context;
    }

    [Fact]
    public void ByDefault_TheAddressIsCollected()
    {
        // 默认必须保持既有行为：这是一个已发布的 API，绝大多数部署依赖它。
        var context = ContextWith(new AspNetCoreOptions());

        Assert.Equal("203.0.113.7", context.Request.GetClientIp());
    }

    [Fact]
    public void WhenCollectionIsDisabled_TheAddressIsNull()
    {
        var context = ContextWith(new AspNetCoreOptions { CollectClientIpAddress = false });

        Assert.Null(context.Request.GetClientIp());
    }

    [Fact]
    public void WhenCollectionIsDisabled_ForwardedHeadersAreIgnoredToo()
    {
        // 反向代理头是最容易被漏掉的一条：关掉采集却仍从 X-Forwarded-For 取值，
        // 等于开关只关了一半，而那一半恰好是生产环境实际使用的那条路径。
        var context = ContextWith(new AspNetCoreOptions { CollectClientIpAddress = false });
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.9, 10.0.0.1";
        context.Request.Headers["X-Real-IP"] = "198.51.100.9";

        Assert.Null(context.Request.GetClientIp());
    }

    [Fact]
    public void WhenTheOptionCannotBeResolved_CollectionContinues()
    {
        // 解析不到选项时按「允许」处理：不能因为容器里没注册它，
        // 就静默改变一个已发布 API 的返回值。真要关闭的部署一定会显式配置。
        var context = ContextWith(options: null);

        Assert.Equal("203.0.113.7", context.Request.GetClientIp());
    }

    [Fact]
    public void TheHttpContextOverload_HonoursTheSwitchToo()
    {
        // 两个重载都是公开面，只改其中一个会留下一条绕过开关的路径。
        var context = ContextWith(new AspNetCoreOptions { CollectClientIpAddress = false });

        Assert.Null(context.GetClientIp());
    }
}
