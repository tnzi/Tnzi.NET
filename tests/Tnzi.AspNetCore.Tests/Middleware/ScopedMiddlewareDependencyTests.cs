using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Modules;
using Tnzi.MultiTenancy;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 约定式中间件是进程级单例：它依赖的 scoped 服务只能按请求解析，不能在构造时从根容器拿。
/// </summary>
/// <remarks>
/// <para>
/// 构造注入 scoped 服务有两张脸：Development（<c>WebApplication.CreateBuilder</c> 默认开作用域校验）
/// 在构建管线时抛「Cannot resolve scoped service ... from root provider」，宿主起不来；
/// Production 不校验，这个服务连同它的 DbContext 被根容器捕获成全进程共享的单例，
/// 两个并发请求同时用它就撞上 EF 的「A second operation was started on this context instance」。
/// </para>
/// <para>
/// 这里全部跑 Development 的真实宿主：直接 new 中间件并传一个 mock 的单测证不了这一点，
/// 这三处正是这样漏掉的。
/// </para>
/// </remarks>
public class ScopedMiddlewareDependencyTests
{
    private const string TenantId = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public async Task RequestValidation_Enabled_HostStarts_AndUnstampedRequestIsRejected()
    {
        await RunAsync(
            new Dictionary<string, string?> { ["AspNetCore:RequestValidation:Enabled"] = "true" },
            async client =>
            {
                Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/e2e/scoped-deps/ping")).StatusCode);

                var stamped = new HttpRequestMessage(HttpMethod.Get, "/api/e2e/scoped-deps/ping");
                stamped.Headers.Add("X-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
                Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(stamped)).StatusCode);
            });
    }

    [Fact]
    public async Task TenantResolver_WithScopedTenantChecker_HostStarts_AndEachRequestGetsItsOwnChecker()
    {
        ScopedDepsProbeStartupModule.EnableMultiTenancy = true;
        try
        {
            await RunAsync(
                new Dictionary<string, string?> { ["AspNetCore:TenantResolver:Enabled"] = "true" },
                async client =>
                {
                    for (var i = 0; i < 3; i++)
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, "/api/e2e/scoped-deps/ping");
                        request.Headers.Add("X-Tenant-Id", TenantId);
                        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
                    }
                });
        }
        finally
        {
            ScopedDepsProbeStartupModule.EnableMultiTenancy = false;
        }

        // 每个请求一个检查器实例：被根容器捕获时三个请求共用同一个。
        Assert.Equal(3, RecordingTenantChecker.Instances.Count);
        Assert.Equal(3, RecordingTenantChecker.Instances.Distinct().Count());
    }

    [Fact]
    public async Task ScopedCustomExceptionHandler_IsResolvedPerRequest()
    {
        await RunAsync(
            [],
            async client =>
            {
                var response = await client.GetAsync("/api/e2e/scoped-deps/boom");
                Assert.Equal((HttpStatusCode)418, response.StatusCode);
                Assert.Contains("handled by the scoped probe handler", await response.Content.ReadAsStringAsync());
            });
    }

    private static async Task RunAsync(Dictionary<string, string?> extraSettings, Func<HttpClient, Task> assertAsync)
    {
        RecordingTenantChecker.Instances.Clear();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();

        var settings = new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false"
        };
        foreach (var (key, value) in extraSettings)
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);

        var app = await TnziApp.CreateAsync<ScopedDepsProbeStartupModule>(builder);
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
}

/// <summary>
/// 注册两个 scoped 依赖：租户检查器与自定义异常处理器（请求校验器用框架自己的 scoped 注册）。
/// </summary>
/// <remarks>静态开关：同一测试类在 xUnit 里串行执行，测试用 finally 复位。</remarks>
[DependsOn(typeof(AspNetCoreModule))]
public sealed class ScopedDepsProbeStartupModule : TnziCustomModule
{
    internal static bool EnableMultiTenancy;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        if (EnableMultiTenancy)
        {
            context.Services.Configure<MultiTenancyOptions>(o => o.Enabled = true);
        }

        context.Services.AddScoped<ITenantChecker, RecordingTenantChecker>();

        context.Services.AddScoped<ScopedProbeExceptionHandler>();
        context.Services.Configure<ExceptionHandlingOptions>(
            o => o.CustomHandlers[typeof(ScopedProbeException)] = typeof(ScopedProbeExceptionHandler));

        return Task.CompletedTask;
    }
}

public sealed class RecordingTenantChecker : ITenantChecker
{
    internal static readonly ConcurrentBag<RecordingTenantChecker> Instances = [];

    public Task<bool> IsActiveAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        Instances.Add(this);
        return Task.FromResult(true);
    }
}

public sealed class ScopedProbeException() : Exception("probe failure");

public sealed class ScopedProbeExceptionHandler : IExceptionHandler<ScopedProbeException>
{
    public Task<ExceptionHandlingResult?> HandleAsync(
        ScopedProbeException exception, HttpContext context, CancellationToken cancellationToken = default)
        => Task.FromResult<ExceptionHandlingResult?>(new ExceptionHandlingResult
        {
            StatusCode = 418,
            Message = "handled by the scoped probe handler",
            ErrorCode = "SCOPED_PROBE"
        });
}

[ApiController]
[Route("e2e/scoped-deps")]
public sealed class ScopedDepsProbeController : ApiControllerBase
{
    [HttpGet("ping")]
    public ApiResult<string> Ping() => Ok("pong", "Success");

    [HttpGet("boom")]
    public ApiResult<string> Boom() => throw new ScopedProbeException();
}
