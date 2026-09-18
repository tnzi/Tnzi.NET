using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 限流中间件必须排在认证之后：它按 <c>ICurrentUser.Id</c> 选 <c>ByUser</c> 规则、
/// 拼 <c>user:{id}</c> 分区键、比对白名单里的用户 ID，而 <c>HttpContext.User</c>
/// 要等 <c>AuthenticationMiddleware</c> 跑过才有 claims。此前限流注册在
/// <c>UseAuthentication()</c> 之前，三处判断对每一个请求都恒假：
/// 配了 <c>ByUser</c> 的部署静默退化成按地址分区，启动校验与配置中心都说它开着。
/// </summary>
/// <remarks>
/// 刻意跑<strong>真实管线</strong>而不是直接调中间件：直接调用时随便塞一个 <c>ICurrentUser</c>
/// 就绿，证不了它在管线里排在认证之后。这条用例把限流挪回认证之前就会红。
/// 认证方案是一个把每个请求都认成同一个用户的测试 handler，
/// 限流存储换成记录键的替身 —— 断言的是「限流看见了谁」，不是「有没有被限」。
/// </remarks>
public class RateLimitPipelineOrderTests
{
    [Fact]
    public async Task AuthenticatedRequest_IsPartitionedByUser_NotByAddress()
    {
        var recorder = new RecordingRateLimitService();
        await RunAsync(recorder, async client =>
        {
            var response = await client.GetAsync("/api/e2e/hello");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });

        var key = Assert.Single(recorder.Keys);
        Assert.StartsWith($"user:{RateLimitOrderProbeAuthHandler.UserId}:", key);
    }

    [Fact]
    public async Task ByUserRule_IsAppliedToAuthenticatedRequest()
    {
        // ByUser 限 1 次，ByIp 限 1000 次：第二个请求被挡下才说明选中的是 ByUser。
        var recorder = new RecordingRateLimitService();
        await RunAsync(
            recorder,
            async client =>
            {
                var first = await client.GetAsync("/api/e2e/hello");
                var second = await client.GetAsync("/api/e2e/hello");

                Assert.Equal(HttpStatusCode.OK, first.StatusCode);
                Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
            },
            new Dictionary<string, string?>
            {
                ["AspNetCore:RateLimit:ByUser:Limit"] = "1",
                ["AspNetCore:RateLimit:ByUser:WindowSeconds"] = "60",
                ["AspNetCore:RateLimit:ByIp:Limit"] = "1000",
                ["AspNetCore:RateLimit:ByIp:WindowSeconds"] = "60"
            });
    }

    [Fact]
    public async Task UserIdInWhitelist_Exempts()
    {
        // TestServer 不带来源地址，所以 MissingPartitionKey 配成 Deny：
        // 限流若仍在认证之前跑，请求既没有用户也没有地址，会被 429 挡下；
        // 排在认证之后，用户 ID 命中白名单，两次都放行且限流存储一次都不被问到。
        // 不配 Deny 的话，「匿名 + 无地址 + Allow」会让这条用例在缺陷仍在时假绿。
        var recorder = new RecordingRateLimitService();
        await RunAsync(
            recorder,
            async client =>
            {
                var first = await client.GetAsync("/api/e2e/hello");
                var second = await client.GetAsync("/api/e2e/hello");

                Assert.Equal(HttpStatusCode.OK, first.StatusCode);
                Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            },
            new Dictionary<string, string?>
            {
                ["AspNetCore:RateLimit:MissingPartitionKey"] = "Deny",
                ["AspNetCore:RateLimit:ByUser:Limit"] = "1",
                ["AspNetCore:RateLimit:ByUser:WindowSeconds"] = "60",
                ["AspNetCore:RateLimit:ByUser:Whitelist:0"] = RateLimitOrderProbeAuthHandler.UserId.ToString()
            });

        // 白名单命中在计数之前放行：限流存储一次都不该被问到。
        Assert.Empty(recorder.Keys);
    }

    [Fact]
    public async Task IpInWhitelist_StillExemptsAuthenticatedCaller()
    {
        // 限流挪到认证之后的回归：已登录请求的标识变成了用户 ID，ByIp 里写的地址
        // 对带凭据的调用方（监控、网关、服务账号）不再命中。地址白名单必须对任何请求都放行。
        // TestServer 本身不带来源地址，由 IStartupFilter 在管线最前面钉上一个。
        var recorder = new RecordingRateLimitService();
        await RunAsync(
            recorder,
            async client =>
            {
                var first = await client.GetAsync("/api/e2e/hello");
                var second = await client.GetAsync("/api/e2e/hello");

                Assert.Equal(HttpStatusCode.OK, first.StatusCode);
                Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            },
            new Dictionary<string, string?>
            {
                ["AspNetCore:RateLimit:MissingPartitionKey"] = "Deny",
                ["AspNetCore:RateLimit:ByIp:Limit"] = "1",
                ["AspNetCore:RateLimit:ByIp:WindowSeconds"] = "60",
                ["AspNetCore:RateLimit:ByIp:Whitelist:0"] = RateLimitOrderProbeRemoteAddressFilter.Address
            },
            remoteAddress: true);

        Assert.Empty(recorder.Keys);
    }

    private static async Task RunAsync(
        RecordingRateLimitService recorder,
        Func<HttpClient, Task> assertAsync,
        Dictionary<string, string?>? extraSettings = null,
        bool remoteAddress = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();

        var settings = new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false",
            ["AspNetCore:RateLimit:Enabled"] = "true",
            ["AspNetCore:RateLimit:DefaultLimit"] = "1000",
            ["AspNetCore:RateLimit:DefaultWindowSeconds"] = "60"
        };
        foreach (var (key, value) in extraSettings ?? [])
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);

        // 同 RateLimitSelfCheckStartupModule：注册必须走模块，builder.Services 进不了最终容器。
        RateLimitOrderProbeStartupModule.Recorder = recorder;
        RateLimitOrderProbeStartupModule.StampRemoteAddress = remoteAddress;
        try
        {
            var app = await TnziApp.CreateAsync<RateLimitOrderProbeStartupModule>(builder);
            await app.StartAsync();
            try
            {
                await assertAsync(app.GetTestClient());
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            RateLimitOrderProbeStartupModule.Recorder = null;
            RateLimitOrderProbeStartupModule.StampRemoteAddress = false;
        }
    }

    /// <summary>真的计数（同一进程内按键累加），所以 ByUser 的 Limit 能被撞到。</summary>
    internal sealed class RecordingRateLimitService : IRateLimitService
    {
        private readonly Dictionary<string, long> _counts = [];

        public List<string> Keys { get; } = [];

        public Task<long> IncrementAndGetAsync(
            string key, int windowSeconds, RateLimitAlgorithm algorithm = RateLimitAlgorithm.FixedWindow)
        {
            lock (_counts)
            {
                Keys.Add(key);
                _counts[key] = _counts.GetValueOrDefault(key) + 1;
                return Task.FromResult(_counts[key]);
            }
        }
    }
}

/// <summary>
/// 只依赖 AspNetCore 的最小启动模块：装一个把每个请求都认成固定用户的认证方案，
/// 并把限流存储换成记录键的替身。
/// </summary>
/// <remarks>静态注入点：同一测试类在 xUnit 里串行执行，helper 用 finally 复位。</remarks>
[DependsOn(typeof(AspNetCoreModule))]
public sealed class RateLimitOrderProbeStartupModule : TnziCustomModule
{
    internal static IRateLimitService? Recorder;

    internal static bool StampRemoteAddress;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        if (StampRemoteAddress)
        {
            context.Services.AddTransient<IStartupFilter, RateLimitOrderProbeRemoteAddressFilter>();
        }

        context.Services
            .AddAuthentication(RateLimitOrderProbeAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, RateLimitOrderProbeAuthHandler>(
                RateLimitOrderProbeAuthHandler.SchemeName, null);

        if (Recorder != null)
        {
            // AspNetCoreModule 用 TryAddScoped 注册真实存储且先于本模块跑，必须显式换掉。
            context.Services.RemoveAll<IRateLimitService>();
            context.Services.AddSingleton(Recorder);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// TestServer 的连接没有来源地址；这个过滤器把一个固定地址钉在管线最前面，
/// 让 <c>GetClientIp()</c>（只读 <c>Connection.RemoteIpAddress</c>）拿得到它。
/// </summary>
public sealed class RateLimitOrderProbeRemoteAddressFilter : IStartupFilter
{
    public const string Address = "203.0.113.7";

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

public sealed class RateLimitOrderProbeAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "RateLimitOrderProbe";

    public static readonly Guid UserId = Guid.Parse("7d9a4c1e-0b6f-4b2a-9c3d-2e1f0a5b6c7d");

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, UserId.ToString()), new Claim(ClaimTypes.Name, "probe")],
            SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
