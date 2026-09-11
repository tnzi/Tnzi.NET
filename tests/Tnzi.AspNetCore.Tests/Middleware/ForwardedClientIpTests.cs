using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Tnzi.AspNetCore.Http;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 谁说了算「客户端地址是什么」。
///
/// 守的是一条会让限流对匿名端点整个失效的线：转发头是**调用方可以随便写的**。
/// 每次请求换一个 `X-Forwarded-For`，限流分区键 `ip:{地址}:{路径}` 就每次落进新桶，
/// 于是配置里写着开着的限流一次都拦不住；白名单同理被打穿。
/// 地址还同时进访问日志、审计、登录日志与连接元数据 —— 那些也就跟着可伪造。
///
/// 正解不是「解析得更聪明」而是**换一个说了算的人**：只有部署方声明过的那一跳
/// （AspNetCore:TrustedProxies）才有资格改写地址，翻译由 UseForwardedHeaders 做，
/// 框架其余部分一律读 Connection.RemoteIpAddress。
/// </summary>
public class ForwardedClientIpTests
{
    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static HttpContext ContextFrom(string remoteIp, AspNetCoreOptions? options = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);

        var services = new ServiceCollection();
        services.AddSingleton<IOptionsMonitor<AspNetCoreOptions>>(
            new StaticMonitor<AspNetCoreOptions>(options ?? new AspNetCoreOptions()));
        context.RequestServices = services.BuildServiceProvider();

        return context;
    }

    /// <summary>把请求过一遍按配置构建的 ForwardedHeaders 中间件，返回它之后的客户端地址。</summary>
    private static async Task<string?> ClientIpBehindProxyAsync(HttpContext context, AspNetCoreOptions options)
    {
        var built = ForwardedHeadersOptionsBuilder.Build(options);
        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Microsoft.Extensions.Options.Options.Create(built));

        await middleware.Invoke(context);

        return context.Request.GetClientIp();
    }

    // ---------------------------------------------------------------
    // 未声明任何受信代理：转发头一律不作数
    // ---------------------------------------------------------------

    [Fact]
    public void AForgedForwardedHeader_DoesNotChangeTheAddress()
    {
        var context = ContextFrom("203.0.113.7");
        context.Request.Headers["X-Forwarded-For"] = "1.2.3.4";

        Assert.Equal("203.0.113.7", context.Request.GetClientIp());
    }

    [Fact]
    public void AForgedRealIpHeader_DoesNotChangeTheAddressEither()
    {
        // 两个头都要挡：只挡 X-Forwarded-For 等于把伪造的入口换了个名字。
        var context = ContextFrom("203.0.113.7");
        context.Request.Headers["X-Real-IP"] = "1.2.3.4";

        Assert.Equal("203.0.113.7", context.Request.GetClientIp());
    }

    [Fact]
    public void EveryForgedHeader_ResolvesToTheSameAddress()
    {
        // 这是限流那条线的直接形态：同一个调用方无论怎么换头，都必须落进同一个桶。
        var seen = new HashSet<string?>();
        foreach (var forged in new[] { "1.2.3.4", "5.6.7.8", "9.10.11.12" })
        {
            var context = ContextFrom("203.0.113.7");
            context.Request.Headers["X-Forwarded-For"] = forged;
            seen.Add(context.Request.GetClientIp());
        }

        Assert.Equal(["203.0.113.7"], seen);
    }

    [Fact]
    public void TheHttpContextOverload_BehavesTheSame()
    {
        var context = ContextFrom("203.0.113.7");
        context.Request.Headers["X-Forwarded-For"] = "1.2.3.4";

        Assert.Equal("203.0.113.7", context.GetClientIp());
    }

    // ---------------------------------------------------------------
    // 声明了受信代理：按代理链解析
    // ---------------------------------------------------------------

    [Fact]
    public async Task ADeclaredProxy_MayRewriteTheAddress()
    {
        var options = new AspNetCoreOptions
        {
            TrustedProxies = new TrustedProxyOptions { KnownProxies = ["10.0.0.8"] }
        };
        var context = ContextFrom("10.0.0.8", options);
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";

        Assert.Equal("203.0.113.7", await ClientIpBehindProxyAsync(context, options));
    }

    [Fact]
    public async Task AnUndeclaredProxy_MayNot()
    {
        // 同一个请求，只是没把这一跳写进声明 —— 地址必须停在代理自己身上。
        // 这是安全的失败方式：限流把所有人算进一个桶（过严），而不是各自一个桶（形同虚设）。
        var options = new AspNetCoreOptions();
        var context = ContextFrom("10.0.0.8", options);
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";

        Assert.Equal("10.0.0.8", await ClientIpBehindProxyAsync(context, options));
    }

    [Fact]
    public async Task ADeclaredNetwork_WorksLikeADeclaredProxy()
    {
        var options = new AspNetCoreOptions
        {
            TrustedProxies = new TrustedProxyOptions { KnownNetworks = ["10.0.0.0/8"] }
        };
        var context = ContextFrom("10.4.5.6", options);
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";

        Assert.Equal("203.0.113.7", await ClientIpBehindProxyAsync(context, options));
    }

    [Fact]
    public async Task TheDefaultForwardLimitStopsAtOneHop()
    {
        // 调用方在自己那条前面再插一项，试图让解析越过我方代理。
        // ForwardLimit=1 时只消费最右一项，取到的是我方代理写下的那个地址。
        var options = new AspNetCoreOptions
        {
            TrustedProxies = new TrustedProxyOptions { KnownProxies = ["10.0.0.8"] }
        };
        var context = ContextFrom("10.0.0.8", options);
        context.Request.Headers["X-Forwarded-For"] = "1.2.3.4, 203.0.113.7";

        Assert.Equal("203.0.113.7", await ClientIpBehindProxyAsync(context, options));
    }

    [Fact]
    public async Task TwoDeclaredHopsNeedAForwardLimitOfTwo()
    {
        var options = new AspNetCoreOptions
        {
            TrustedProxies = new TrustedProxyOptions
            {
                KnownProxies = ["10.0.0.8", "10.0.0.9"],
                ForwardLimit = 2
            }
        };
        var context = ContextFrom("10.0.0.8", options);
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7, 10.0.0.9";

        Assert.Equal("203.0.113.7", await ClientIpBehindProxyAsync(context, options));
    }

    [Fact]
    public async Task TrustingEveryProxy_AcceptsAnUndeclaredHop()
    {
        // 部署方明说「只有代理到得了本进程」时的逃生口。它是一句承诺，不是一个调优项。
        var options = new AspNetCoreOptions
        {
            TrustedProxies = new TrustedProxyOptions { TrustAllProxies = true }
        };
        var context = ContextFrom("198.51.100.4", options);
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";

        Assert.Equal("203.0.113.7", await ClientIpBehindProxyAsync(context, options));
    }

    [Fact]
    public async Task ADeploymentMayPointAtTheRealIpHeaderInstead()
    {
        var options = new AspNetCoreOptions
        {
            TrustedProxies = new TrustedProxyOptions
            {
                KnownProxies = ["10.0.0.8"],
                ForwardedForHeaderName = "X-Real-IP"
            }
        };
        var context = ContextFrom("10.0.0.8", options);
        context.Request.Headers["X-Real-IP"] = "203.0.113.7";

        Assert.Equal("203.0.113.7", await ClientIpBehindProxyAsync(context, options));
    }

    // ---------------------------------------------------------------
    // 构建期
    // ---------------------------------------------------------------

    [Fact]
    public void ByDefault_OnlyLoopbackIsTrusted()
    {
        // 出厂默认即「代理与应用同机」。它必须是默认值本身，
        // 而不是「碰巧没人配过所以什么都不信」—— 后者会让同机部署也拿不到真实地址。
        var built = ForwardedHeadersOptionsBuilder.Build(new AspNetCoreOptions());

        Assert.Contains(built.KnownProxies, ip => IPAddress.IsLoopback(ip));
    }

    [Fact]
    public void ADeclaredProxyIsAddedOnTopOfTheDefaults()
    {
        var built = ForwardedHeadersOptionsBuilder.Build(new AspNetCoreOptions
        {
            TrustedProxies = new TrustedProxyOptions { KnownProxies = ["10.0.0.8"] }
        });

        Assert.Contains(IPAddress.Parse("10.0.0.8"), built.KnownProxies);
        Assert.Contains(built.KnownProxies, ip => IPAddress.IsLoopback(ip));
    }

    [Fact]
    public void TrustingEveryProxy_StillKeepsExplicitlyDeclaredOnes()
    {
        // 清空必须发生在追加之前。反过来会把刚加进去的一起清掉，
        // 而外观上配置是「生效」的。
        var built = ForwardedHeadersOptionsBuilder.Build(new AspNetCoreOptions
        {
            TrustedProxies = new TrustedProxyOptions
            {
                TrustAllProxies = true,
                KnownProxies = ["10.0.0.8"]
            }
        });

        Assert.Contains(IPAddress.Parse("10.0.0.8"), built.KnownProxies);
        Assert.Empty(built.KnownIPNetworks);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("10.0.0.256")]
    public void AMalformedProxyAddress_FailsTheBuild(string proxy)
    {
        // 跳过它的症状是「限流突然把所有人算在一起」,没有人会把那个联想到一个拼错的地址。
        var options = new AspNetCoreOptions
        {
            TrustedProxies = new TrustedProxyOptions { KnownProxies = [proxy] }
        };

        Assert.Throws<TnziException>(() => ForwardedHeadersOptionsBuilder.Build(options));
    }

    [Theory]
    [InlineData("10.0.0.0")]
    [InlineData("10.0.0.0/x")]
    public void AMalformedNetwork_FailsTheBuild(string network)
    {
        var options = new AspNetCoreOptions
        {
            TrustedProxies = new TrustedProxyOptions { KnownNetworks = [network] }
        };

        Assert.Throws<TnziException>(() => ForwardedHeadersOptionsBuilder.Build(options));
    }

    [Fact]
    public void CollectionRemainsDisableable()
    {
        // 隐私开关仍然优先于一切：声明了受信代理也不该让地址重新被采集。
        var options = new AspNetCoreOptions
        {
            CollectClientIpAddress = false,
            TrustedProxies = new TrustedProxyOptions { TrustAllProxies = true }
        };
        var context = ContextFrom("203.0.113.7", options);

        Assert.Null(context.Request.GetClientIp());
    }
}
