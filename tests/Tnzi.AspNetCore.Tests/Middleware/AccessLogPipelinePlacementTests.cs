using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tnzi.Hosting;
using Tnzi.Modules;
using Tnzi.System;
using Tnzi.System.Dtos;
using Tnzi.System.Services;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// <c>Sys_AccessLog</c> 的采集器必须看得见<b>被拒绝</b>的请求：401 / 403 / 429 都要落到队列里。
/// </summary>
/// <remarks>
/// <para>
/// ★ 采集器曾由 <c>SystemModule.OnApplicationInitializationAsync</c> 追加在 AspNetCore 管线之后 = 授权之后。
/// 授权与限流中间件对被拒绝的请求就地写出响应、不再调用下游，于是那个位置只看得见通过了认证、限流与授权的请求：
/// 未认证、无权限、被限流的一条都记不到，错误统计与 Top 错误端点整体失真，管理员读到的是「没有人被拒绝过」。
/// 现在它经 <c>AddRequestPipelineMiddleware</c> 登记在 <see cref="RequestPipelineStage.AfterAuthentication"/>。
/// </para>
/// <para>
/// 刻意<b>把应用真的启起来</b>（TestServer + 真实模块图）而不是拼一条假管线：插入点的全部价值在于它在固定管线里的位置，
/// 单测拼出来的顺序证明不了 AspNetCoreModule 真的把它放在了限流与授权之前。
/// </para>
/// </remarks>
public class AccessLogPipelinePlacementTests
{
    private const string UserHeader = "X-Test-User";

    [Fact]
    public async Task UnauthenticatedRequest_IsRecordedAs401()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync("/api/e2e/access-log/protected");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var log = await host.WaitForCaptureAsync("/api/e2e/access-log/protected");
        Assert.Equal(401, log.StatusCode);
        Assert.Null(log.UserId);
    }

    [Fact]
    public async Task ForbiddenRequest_IsRecordedAs403_WithTheUser()
    {
        await using var host = await StartAsync();
        var userId = Guid.NewGuid();
        host.Client.DefaultRequestHeaders.Add(UserHeader, userId.ToString());

        var response = await host.Client.GetAsync("/api/e2e/access-log/admin-only");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var log = await host.WaitForCaptureAsync("/api/e2e/access-log/admin-only");
        Assert.Equal(403, log.StatusCode);
        // 插入点在认证之后：被拒绝的那个人是谁也记下来了。
        Assert.Equal(userId, log.UserId);
    }

    [Fact]
    public async Task RateLimitedRequest_IsRecordedAs429()
    {
        // 匿名请求拿不到分区键时拒绝：不采集来源地址 + Deny，每一个匿名请求都是 429。
        await using var host = await StartAsync(new Dictionary<string, string?>
        {
            ["AspNetCore:RateLimit:Enabled"] = "true",
            ["AspNetCore:RateLimit:MissingPartitionKey"] = "Deny",
            ["AspNetCore:CollectClientIpAddress"] = "false",
        });

        var response = await host.Client.GetAsync("/api/e2e/access-log/open");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var log = await host.WaitForCaptureAsync("/api/e2e/access-log/open");
        Assert.Equal(429, log.StatusCode);
    }

    /// <summary>对照：放行的请求照常记 200，且带上用户。</summary>
    [Fact]
    public async Task AllowedRequest_IsRecordedAs200()
    {
        await using var host = await StartAsync();
        var userId = Guid.NewGuid();
        host.Client.DefaultRequestHeaders.Add(UserHeader, userId.ToString());

        var response = await host.Client.GetAsync("/api/e2e/access-log/protected");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var log = await host.WaitForCaptureAsync("/api/e2e/access-log/protected");
        Assert.Equal(200, log.StatusCode);
        Assert.Equal(userId, log.UserId);
    }

    private static async Task<RunningHost> StartAsync(Dictionary<string, string?>? extraSettings = null)
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
            ["System:AccessLog:Enabled"] = "true",
        };
        foreach (var (key, value) in extraSettings ?? [])
        {
            settings[key] = value;
        }
        builder.Configuration.AddInMemoryCollection(settings);

        var sender = new CapturingAccessLogSender();
        AccessLogPlacementStartupModule.Sender = sender;
        try
        {
            var app = await TnziApp.CreateAsync<AccessLogPlacementStartupModule>(builder);
            await app.StartAsync();
            return new RunningHost(app, sender);
        }
        finally
        {
            AccessLogPlacementStartupModule.Sender = null;
        }
    }

    private sealed class RunningHost(WebApplication app, CapturingAccessLogSender sender) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();

        /// <summary>采集发生在响应写出之后的 finally 里，客户端可能先拿到响应；等一小会儿。</summary>
        public async Task<AccessLogDto> WaitForCaptureAsync(string path)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var match = sender.Snapshot().FirstOrDefault(l => l.Path == path);
                if (match != null)
                {
                    return match;
                }

                await Task.Delay(20);
            }

            throw new Xunit.Sdk.XunitException($"No access log entry was captured for {path}. Captured: [{string.Join(", ", sender.Snapshot().Select(l => l.Path))}]");
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    internal sealed class CapturingAccessLogSender : IAccessLogSender
    {
        private readonly List<AccessLogDto> _captured = [];

        public Task SendAsync(AccessLogDto log)
        {
            lock (_captured)
            {
                _captured.Add(log);
            }

            return Task.CompletedTask;
        }

        public List<AccessLogDto> Snapshot()
        {
            lock (_captured)
            {
                return [.. _captured];
            }
        }
    }

    /// <summary>带 <c>X-Test-User</c> 头即视为该用户（无角色），否则匿名。挑战 401、拒绝 403 走基类默认。</summary>
    internal sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestHeader";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var userId) || string.IsNullOrEmpty(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId!), new Claim(ClaimTypes.Name, "tester")],
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}

[DependsOn(typeof(SystemModule))]
public sealed class AccessLogPlacementStartupModule : HostingModule
{
    internal static AccessLogPipelinePlacementTests.CapturingAccessLogSender? Sender;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        HostingTestSetup.RegisterCommonMocks(context.Services);

        if (Sender != null)
        {
            // 换掉发送者才看得见中间件投了什么；后台消费者仍读真实发送者的空队列，无副作用。
            context.Services.RemoveAll<IAccessLogSender>();
            context.Services.AddSingleton<IAccessLogSender>(Sender);
        }

        context.Services
            .AddAuthentication(AccessLogPipelinePlacementTests.HeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, AccessLogPipelinePlacementTests.HeaderAuthenticationHandler>(
                AccessLogPipelinePlacementTests.HeaderAuthenticationHandler.SchemeName, _ => { });

        return base.ConfigureServicesAsync(context);
    }
}

[ApiController]
[Route("e2e/access-log/open")]
public sealed class AccessLogOpenController : ApiControllerBase
{
    [HttpGet]
    public ApiResult<string> Get() => Ok("open", "Success");
}

[ApiController]
[Route("e2e/access-log/protected")]
[Authorize]
public sealed class AccessLogProtectedController : ApiControllerBase
{
    [HttpGet]
    public ApiResult<string> Get() => Ok("protected", "Success");
}

[ApiController]
[Route("e2e/access-log/admin-only")]
[Authorize(Roles = "admin")]
public sealed class AccessLogAdminOnlyController : ApiControllerBase
{
    [HttpGet]
    public ApiResult<string> Get() => Ok("admin-only", "Success");
}
