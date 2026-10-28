using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 限流额度按端点算，不按请求写成的那串 URL 算。
/// </summary>
/// <remarks>
/// 路由对字面段不分大小写、忽略末尾斜杠，而限流存储的键区分大小写。分区键若用原始路径，
/// <c>/api/auth/login</c>、<c>/api/auth/Login</c>、<c>/API/auth/login/</c> 打到同一个 action 却各算各的额度，
/// <c>/api/users/1</c>、<c>/api/users/2</c> 每个 id 一个新桶：只配了 <c>ByIp</c> / <c>ByUser</c> /
/// <c>DefaultLimit</c> 的部署，限流对一个肯换写法的调用方永远不触发。
/// <para>
/// 前四条跑<strong>真实管线</strong>：它们同时证明限流跑到时端点已经选出（路由在管线最前面），
/// 直接调中间件时手动塞一个端点证不了这一点。
/// </para>
/// </remarks>
public class RateLimitEndpointBucketTests
{
    private const string Address = RateLimitOrderProbeRemoteAddressFilter.Address;

    [Fact]
    public async Task CaseAndTrailingSlashVariants_OfOneEndpoint_ShareOneBucket()
    {
        var recorder = new RateLimitPipelineOrderTests.RecordingRateLimitService();
        await RunAsync(recorder, ByIp(limit: 2), async client =>
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/e2e/rate-bucket/login")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/E2E/rate-bucket/Login")).StatusCode);
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await client.GetAsync("/API/e2e/RATE-BUCKET/login/")).StatusCode);
        });

        Assert.All(recorder.Keys, key => Assert.Equal($"ip:{Address}:/api/e2e/rate-bucket/login", key));
    }

    [Fact]
    public async Task RouteParameterValues_ShareTheEndpointBucket()
    {
        var recorder = new RateLimitPipelineOrderTests.RecordingRateLimitService();
        await RunAsync(recorder, ByIp(limit: 2), async client =>
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/e2e/rate-bucket/items/1")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/e2e/rate-bucket/items/2")).StatusCode);
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await client.GetAsync("/api/e2e/rate-bucket/items/3")).StatusCode);
        });

        Assert.All(recorder.Keys, key => Assert.Equal($"ip:{Address}:/api/e2e/rate-bucket/items/{{id}}", key));
    }

    [Fact]
    public async Task DistinctEndpoints_KeepSeparateBuckets()
    {
        // 规范化只能合并「同一个端点的不同写法」，不能把不同端点并进一个桶。
        var recorder = new RateLimitPipelineOrderTests.RecordingRateLimitService();
        await RunAsync(recorder, ByIp(limit: 1), async client =>
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/e2e/rate-bucket/login")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/e2e/rate-bucket/items/1")).StatusCode);
        });

        Assert.Equal(2, recorder.Keys.Distinct().Count());
    }

    [Fact]
    public async Task UnmatchedPaths_DifferingOnlyInCaseOrTrailingSlash_ShareABucket()
    {
        // 没有命中端点（404）时退到规范化路径：同样不能被换写法绕过。
        var recorder = new RateLimitPipelineOrderTests.RecordingRateLimitService();
        await RunAsync(recorder, ByIp(limit: 1), async client =>
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/no-such/Thing")).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/NO-SUCH/thing/")).StatusCode);
        });

        Assert.All(recorder.Keys, key => Assert.Equal($"ip:{Address}:/api/no-such/thing", key));
    }

    [Fact]
    public async Task UnderAPathBase_TheEndpointIsStillResolvedForTheKey()
    {
        // 隐式的 UseRouting 排在 UsePathBase 之前；UsePathBase 剥掉前缀后重新路由，限流看到的必须是重新选出的端点
        var settings = ByIp(limit: 1);
        settings["AspNetCore:PathBase"] = "/subapp";

        var recorder = new RateLimitPipelineOrderTests.RecordingRateLimitService();
        await RunAsync(recorder, settings, async client =>
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/subapp/api/e2e/rate-bucket/items/1")).StatusCode);
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                (await client.GetAsync("/subapp/api/E2E/rate-bucket/items/2")).StatusCode);
        });

        Assert.All(recorder.Keys, key => Assert.Equal($"ip:{Address}:/api/e2e/rate-bucket/items/{{id}}", key));
    }

    [Fact]
    public async Task ByPathRule_StillKeysOnTheConfiguredPrefix()
    {
        // ByPath 的键本来就是配置的前缀（整个前缀共用一个桶），不受这次改动影响。
        var settings = ByIp(limit: 1000);
        settings["AspNetCore:RateLimit:ByPath:/api/e2e/rate-bucket:Limit"] = "1000";
        settings["AspNetCore:RateLimit:ByPath:/api/e2e/rate-bucket:WindowSeconds"] = "60";

        var recorder = new RateLimitPipelineOrderTests.RecordingRateLimitService();
        await RunAsync(recorder, settings, async client =>
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/e2e/rate-bucket/items/1")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/E2E/Rate-Bucket/login/")).StatusCode);
        });

        Assert.All(recorder.Keys, key => Assert.Equal($"ip:{Address}:/api/e2e/rate-bucket", key));
    }

    // ---- 约定路由 -------------------------------------------------------------
    //
    // 约定路由的每个 action 都是独立端点，却共用同一段原始模板
    // （{controller=Home}/{action=Index}/{id?}）。直接拿原始模板当键，
    // 所有约定路由的 action 会被并进一个桶；必须把端点的必需值代回参数。

    [Fact]
    public async Task ConventionalRouteActions_EachGetTheirOwnBucket()
    {
        var users = await KeyForEndpointAsync(ConventionalEndpoint("Users", "List"), "/Users/List/7");
        var orders = await KeyForEndpointAsync(ConventionalEndpoint("Orders", "List"), "/orders/list");

        Assert.Equal($"ip:{Address}:/users/list/{{id}}", users);
        Assert.Equal($"ip:{Address}:/orders/list/{{id}}", orders);
    }

    [Fact]
    public async Task ConventionalRouteAction_ReachedThroughItsDefaults_SharesTheBucket()
    {
        // "/" 与 "/Home/Index" 是同一个端点（靠默认值匹配），额度必须是同一份。
        var endpoint = ConventionalEndpoint("Home", "Index");

        Assert.Equal(await KeyForEndpointAsync(endpoint, "/"), await KeyForEndpointAsync(endpoint, "/Home/Index/"));
    }

    private static RouteEndpoint ConventionalEndpoint(string controller, string action)
        => new(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(
                "{controller=Home}/{action=Index}/{id?}",
                defaults: null,
                parameterPolicies: null,
                requiredValues: new { controller, action }),
            order: 0,
            metadata: null,
            displayName: $"{controller}.{action}");

    private static async Task<string> KeyForEndpointAsync(Endpoint endpoint, string path)
    {
        var options = new AspNetCoreOptions
        {
            RateLimit = new RateLimitOptions { Enabled = true, DefaultLimit = 10, DefaultWindowSeconds = 60 }
        };
        var monitor = Mock.Of<IOptionsMonitor<AspNetCoreOptions>>(m => m.CurrentValue == options);
        var middleware = new RateLimitingMiddleware(
            _ => Task.CompletedTask, monitor, new Mock<ILogger<RateLimitingMiddleware>>().Object);

        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse(Address);
        context.SetEndpoint(endpoint);
        context.RequestServices = new ServiceCollection()
            .AddSingleton(monitor)
            .BuildServiceProvider();

        var recorder = new RateLimitPipelineOrderTests.RecordingRateLimitService();
        await middleware.InvokeAsync(context, recorder);

        return Assert.Single(recorder.Keys);
    }

    private static Dictionary<string, string?> ByIp(int limit) => new()
    {
        ["AspNetCore:RateLimit:MissingPartitionKey"] = "Deny",
        ["AspNetCore:RateLimit:ByIp:Limit"] = limit.ToString(),
        ["AspNetCore:RateLimit:ByIp:WindowSeconds"] = "60"
    };

    private static async Task RunAsync(
        RateLimitPipelineOrderTests.RecordingRateLimitService recorder,
        Dictionary<string, string?> extraSettings,
        Func<HttpClient, Task> assertAsync)
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
        foreach (var (key, value) in extraSettings)
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);

        RateLimitBucketProbeStartupModule.Recorder = recorder;
        try
        {
            var app = await TnziApp.CreateAsync<RateLimitBucketProbeStartupModule>(builder);
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
            RateLimitBucketProbeStartupModule.Recorder = null;
        }
    }
}

/// <summary>
/// 匿名请求、固定来源地址、记录键的限流存储：分区键恒为 <c>ip:{地址}:{路径}</c>，
/// 断言只看路径维度。
/// </summary>
/// <remarks>静态注入点：同一测试类在 xUnit 里串行执行，helper 用 finally 复位。</remarks>
[DependsOn(typeof(AspNetCoreModule))]
public sealed class RateLimitBucketProbeStartupModule : TnziCustomModule
{
    internal static IRateLimitService? Recorder;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<IStartupFilter, RateLimitOrderProbeRemoteAddressFilter>();

        if (Recorder != null)
        {
            // AspNetCoreModule 用 TryAddScoped 注册真实存储且先于本模块跑，必须显式换掉。
            context.Services.RemoveAll<IRateLimitService>();
            context.Services.AddSingleton(Recorder);
        }

        return Task.CompletedTask;
    }
}

[ApiController]
[Route("e2e/rate-bucket")]
public sealed class RateLimitBucketProbeController : ApiControllerBase
{
    [HttpGet("login")]
    public ApiResult<string> Login() => Ok("login", "Success");

    [HttpGet("items/{id}")]
    public ApiResult<string> Item(string id) => Ok(id, "Success");
}
