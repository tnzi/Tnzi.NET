using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.AspNetCore.Http;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.Http;

/// <summary>
/// 谁说了算「应用挂在哪个子路径下」。
///
/// <c>X-Forwarded-Prefix</c> 与 <c>X-Forwarded-For</c> 是同一批转发头：只有部署方声明过的那一跳
/// （<c>AspNetCore:TrustedProxies</c>）才有资格改写 <c>Request.PathBase</c>。此前 For/Proto/Host
/// 走受信代理判定，Prefix 却由一段自建中间件无条件采信 —— 任何直连调用方发一个
/// <c>X-Forwarded-Prefix: //attacker.example</c>，欢迎页三条链接、管理端 hub 路径、
/// OAuth 回调地址全部跟着改写，且整体覆盖掉配置里的 <c>AspNetCore:PathBase</c>。
/// </summary>
/// <remarks>
/// 跑真实管线：直接调 <c>ForwardedHeadersMiddleware</c> 证不了框架没有在它旁边再开一个口子。
/// TestServer 的连接没有来源地址，而 <c>ForwardedHeadersMiddleware</c> 对<b>没有</b>来源地址的连接
/// 不做受信判定 —— 所以每条用例都先经 <see cref="ForwardedPrefixRemoteAddressFilter"/>
/// 钉上一个公网地址，否则「未声明受信代理」那几条会假绿。
/// </remarks>
public class ForwardedPrefixTests
{
    private const string ProbePath = "/api/e2e/forwarded-prefix";

    [Fact]
    public void Builder_ConsumesTheForwardedPrefixHeader()
    {
        // 翻译只能由 UseForwardedHeaders 做：这是它被授权消费的头之一。
        var built = ForwardedHeadersOptionsBuilder.Build(new AspNetCoreOptions());

        Assert.True(built.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedPrefix));
        Assert.Equal("X-Forwarded-Prefix", built.ForwardedPrefixHeaderName);
    }

    [Fact]
    public async Task UntrustedCaller_ForwardedPrefix_IsIgnored()
    {
        // 没有声明任何受信代理：一个公网调用方自称的前缀一律不作数。
        var pathBase = await ProbePathBaseAsync(
            settings: [],
            requestPath: ProbePath,
            forwardedPrefix: "//attacker.example");

        Assert.Equal(string.Empty, pathBase);
    }

    [Fact]
    public async Task UntrustedCaller_CannotOverrideConfiguredPathBase()
    {
        // 部署级的 AspNetCore:PathBase 不能被单个请求头覆写。
        var pathBase = await ProbePathBaseAsync(
            settings: new Dictionary<string, string?> { ["AspNetCore:PathBase"] = "/api" },
            requestPath: "/api" + ProbePath,
            forwardedPrefix: "/evil");

        Assert.Equal("/api", pathBase);
    }

    [Fact]
    public async Task TrustedProxy_ForwardedPrefix_SetsPathBase()
    {
        var pathBase = await ProbePathBaseAsync(
            settings: new Dictionary<string, string?>
            {
                ["AspNetCore:TrustedProxies:KnownProxies:0"] = ForwardedPrefixRemoteAddressFilter.Address
            },
            requestPath: ProbePath,
            forwardedPrefix: "/behind-proxy");

        Assert.Equal("/behind-proxy", pathBase);
    }

    [Fact]
    public async Task TrustedProxy_ForwardedPrefix_ReplacesConfiguredPathBase()
    {
        // 记录内建中间件的语义：受信代理给的前缀**整体替换** Request.PathBase，
        // 不是追加在配置的 PathBase 之后。两者同时存在时以代理为准（它更靠近真相：
        // 配置写的是「我以为挂在哪」，代理报的是「请求实际从哪进来」）。
        var pathBase = await ProbePathBaseAsync(
            settings: new Dictionary<string, string?>
            {
                ["AspNetCore:PathBase"] = "/api",
                ["AspNetCore:TrustedProxies:TrustAllProxies"] = "true"
            },
            requestPath: "/api" + ProbePath,
            forwardedPrefix: "/edge");

        Assert.Equal("/edge", pathBase);
    }

    [Fact]
    public async Task ForwardedHeadersDisabled_IgnoresThePrefixEvenFromATrustedProxy()
    {
        var pathBase = await ProbePathBaseAsync(
            settings: new Dictionary<string, string?>
            {
                ["AspNetCore:EnableForwardedHeaders"] = "false",
                ["AspNetCore:TrustedProxies:TrustAllProxies"] = "true"
            },
            requestPath: ProbePath,
            forwardedPrefix: "/behind-proxy");

        Assert.Equal(string.Empty, pathBase);
    }

    private static async Task<string> ProbePathBaseAsync(
        Dictionary<string, string?> settings, string requestPath, string forwardedPrefix)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();

        var allSettings = new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "true"
        };
        foreach (var (key, value) in settings)
        {
            allSettings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(allSettings);

        var app = await TnziApp.CreateAsync<ForwardedPrefixStartupModule>(builder);
        await app.StartAsync();

        try
        {
            var client = app.GetTestClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
            request.Headers.Add("X-Forwarded-Prefix", forwardedPrefix);

            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return envelope.RootElement.GetProperty("data").GetString() ?? string.Empty;
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}

/// <summary>只依赖 AspNetCore 的最小启动模块，管线最前面钉一个固定的公网来源地址。</summary>
[DependsOn(typeof(AspNetCoreModule))]
public sealed class ForwardedPrefixStartupModule : TnziCustomModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<IStartupFilter, ForwardedPrefixRemoteAddressFilter>();
        return Task.CompletedTask;
    }
}

/// <summary>
/// TestServer 的连接没有来源地址，而 <c>ForwardedHeadersMiddleware</c> 对没有地址的连接
/// 跳过受信判定。钉上一个公网地址，「未声明受信代理」才真的是未受信。
/// </summary>
public sealed class ForwardedPrefixRemoteAddressFilter : IStartupFilter
{
    public const string Address = "198.51.100.23";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(Address);
                return nextMiddleware(context);
            });
            next(app);
        };
}

/// <summary>把这次请求看到的 <c>Request.PathBase</c> 原样回给调用方。</summary>
[ApiController]
[Route("e2e/forwarded-prefix")]
public sealed class ForwardedPrefixProbeController : ApiControllerBase
{
    [HttpGet]
    public ApiResult<string> Get()
    {
        return Ok(Request.PathBase.Value ?? string.Empty, "Success");
    }
}
