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
using Tnzi.Audit;
using Tnzi.Audit.Entities;
using Tnzi.Audit.Metadata;
using Tnzi.Audit.Services;
using Tnzi.Hosting;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 操作审计必须看得见<b>被拒绝</b>的请求：未认证 401、无权限 403、被限流 429 都要产出一条 <c>Audit_Operation</c>。
/// </summary>
/// <remarks>
/// <para>
/// ★ 审计中间件曾由 <c>AuditModule.OnApplicationInitializationAsync</c> 追加在 AspNetCore 管线之后 = 授权之后。
/// 授权与限流中间件对被拒绝的请求就地写出响应、不再调用下游，于是一个逐个试探管理端写接口的账号
/// 每次都拿到 403，而审计表里一行都没有。现在它经 <c>AddRequestPipelineMiddleware</c> 登记在
/// <see cref="RequestPipelineStage.AfterAuthentication"/>。
/// </para>
/// <para>
/// 刻意把应用真的启起来（TestServer + 真实模块图）：插入点的价值全在它在固定管线里的位置。
/// </para>
/// </remarks>
public class AuditPipelinePlacementTests
{
    private const string UserHeader = "X-Test-User";

    [Fact]
    public async Task UnauthenticatedRequest_IsAuditedAsFailed401()
    {
        await using var host = await StartAsync();

        var response = await host.Client.PostAsync("/api/e2e/audit/protected", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var operation = await host.WaitForCaptureAsync("/api/e2e/audit/protected");
        Assert.Equal(401, operation.HttpStatusCode);
        Assert.Equal(AuditResultType.Failed, operation.ResultType);
        Assert.Null(operation.UserId);
    }

    [Fact]
    public async Task ForbiddenRequest_IsAuditedAsFailed403_WithTheUser()
    {
        await using var host = await StartAsync();
        var userId = Guid.NewGuid();
        host.Client.DefaultRequestHeaders.Add(UserHeader, userId.ToString());

        var response = await host.Client.PostAsync("/api/e2e/audit/admin-only", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var operation = await host.WaitForCaptureAsync("/api/e2e/audit/admin-only");
        Assert.Equal(403, operation.HttpStatusCode);
        Assert.Equal(AuditResultType.Failed, operation.ResultType);
        // 插入点在认证之后：试探权限的那个人是谁也记下来了。
        Assert.Equal(userId, operation.UserId);
    }

    [Fact]
    public async Task RateLimitedRequest_IsAuditedAsFailed429()
    {
        // 匿名请求拿不到分区键时拒绝：不采集来源地址 + Deny，每一个匿名请求都是 429。
        await using var host = await StartAsync(new Dictionary<string, string?>
        {
            ["AspNetCore:RateLimit:Enabled"] = "true",
            ["AspNetCore:RateLimit:MissingPartitionKey"] = "Deny",
            ["AspNetCore:CollectClientIpAddress"] = "false",
        });

        var response = await host.Client.PostAsync("/api/e2e/audit/open", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var operation = await host.WaitForCaptureAsync("/api/e2e/audit/open");
        Assert.Equal(429, operation.HttpStatusCode);
        Assert.Equal(AuditResultType.Failed, operation.ResultType);
    }

    /// <summary>对照：放行的请求照常记成功，端点分类（功能名 / 写操作）在这个插入点上照样拿得到端点元数据。</summary>
    [Fact]
    public async Task AllowedRequest_IsAuditedAsSuccess_WithEndpointClassification()
    {
        await using var host = await StartAsync();
        var userId = Guid.NewGuid();
        host.Client.DefaultRequestHeaders.Add(UserHeader, userId.ToString());

        var response = await host.Client.PostAsync("/api/e2e/audit/protected", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var operation = await host.WaitForCaptureAsync("/api/e2e/audit/protected");
        Assert.Equal(200, operation.HttpStatusCode);
        Assert.Equal(AuditResultType.Success, operation.ResultType);
        Assert.Equal(userId, operation.UserId);
        Assert.Equal("AuditProtected.Post", operation.FunctionName);
        Assert.True(operation.IsWrite);
    }

    /// <summary>对照：端点上的 <c>[AuditDisabled]</c> 在这个插入点上仍然生效（路由已经跑过，端点元数据可读）。</summary>
    [Fact]
    public async Task AuditDisabledEndpoint_IsStillSkipped()
    {
        await using var host = await StartAsync();
        host.Client.DefaultRequestHeaders.Add(UserHeader, Guid.NewGuid().ToString());

        var response = await host.Client.PostAsync("/api/e2e/audit/disabled", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 再发一条会被记下的请求作哨兵：它到了，前一条若会被记也早该到了。
        await host.Client.PostAsync("/api/e2e/audit/open", null);
        await host.WaitForCaptureAsync("/api/e2e/audit/open");

        Assert.DoesNotContain(host.Snapshot(), o => o.Url == "/api/e2e/audit/disabled");
    }

    /// <summary>
    /// 路径里的令牌（签署链接、分享链接的 <c>{token}</c>）不得原样落进 <c>Audit_Operation.Url</c>：
    /// 那张表持 <c>audit.operation.view</c> 的人都读得到，而令牌本身就是能用的凭据。
    /// </summary>
    [Fact]
    public async Task ATokenInThePath_IsMaskedInTheAuditedUrl()
    {
        const string token = "Zq3x-SECRET-LINK-TOKEN-0123456789abcdefABCDEF";
        await using var host = await StartAsync();
        host.Client.DefaultRequestHeaders.Add(UserHeader, Guid.NewGuid().ToString());

        var response = await host.Client.PostAsync($"/api/e2e/audit/links/{token}/accept?lang=en&token={token}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var operation = await host.WaitForCaptureAsync("/api/e2e/audit/links/***/accept?lang=en&token=***");
        Assert.DoesNotContain(token, operation.Url);
        Assert.DoesNotContain(token, operation.FunctionName);
        Assert.DoesNotContain(token, operation.RequestParameters ?? string.Empty);
    }

    private static async Task<RunningHost> StartAsync(Dictionary<string, string?>? extraSettings = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        // 审计模块的存储服务要仓储，而本用例不建库：发送者换成了捕获替身，存储一侧永远不会被解析到。
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = false;
            options.ValidateScopes = false;
        });
        builder.WebHost.UseTestServer();
        var settings = new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false",
        };
        foreach (var (key, value) in extraSettings ?? [])
        {
            settings[key] = value;
        }
        builder.Configuration.AddInMemoryCollection(settings);

        var sender = new CapturingAuditSender();
        AuditPlacementStartupModule.Sender = sender;
        try
        {
            var app = await TnziApp.CreateAsync<AuditPlacementStartupModule>(builder);
            await app.StartAsync();
            return new RunningHost(app, sender);
        }
        finally
        {
            AuditPlacementStartupModule.Sender = null;
        }
    }

    private sealed class RunningHost(WebApplication app, CapturingAuditSender sender) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();

        public List<AuditOperation> Snapshot() => sender.Snapshot();

        /// <summary>入队发生在响应写出之后的 finally 里，客户端可能先拿到响应；等一小会儿。</summary>
        public async Task<AuditOperation> WaitForCaptureAsync(string url)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var match = sender.Snapshot().FirstOrDefault(o => o.Url == url);
                if (match != null)
                {
                    return match;
                }

                await Task.Delay(20);
            }

            throw new Xunit.Sdk.XunitException($"No audit operation was captured for {url}. Captured: [{string.Join(", ", sender.Snapshot().Select(o => o.Url))}]");
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    internal sealed class CapturingAuditSender : IAuditSender
    {
        private readonly List<AuditOperation> _captured = [];

        public Task SendAsync(AuditOperation operation)
        {
            lock (_captured)
            {
                _captured.Add(operation);
            }

            return Task.CompletedTask;
        }

        public List<AuditOperation> Snapshot()
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
        public const string SchemeName = "TestAuditHeader";

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

[DependsOn(typeof(AuditModule))]
public sealed class AuditPlacementStartupModule : HostingModule
{
    internal static AuditPipelinePlacementTests.CapturingAuditSender? Sender;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        HostingTestSetup.RegisterCommonMocks(context.Services);

        if (Sender != null)
        {
            // 换掉发送者才看得见中间件投了什么；后台消费者仍读真实发送者的空队列，无副作用。
            context.Services.RemoveAll<IAuditSender>();
            context.Services.AddSingleton<IAuditSender>(Sender);
        }

        context.Services
            .AddAuthentication(AuditPipelinePlacementTests.HeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, AuditPipelinePlacementTests.HeaderAuthenticationHandler>(
                AuditPipelinePlacementTests.HeaderAuthenticationHandler.SchemeName, _ => { });

        return base.ConfigureServicesAsync(context);
    }
}

[ApiController]
[Route("e2e/audit/open")]
public sealed class AuditOpenController : ApiControllerBase
{
    [HttpPost]
    public ApiResult<string> Post() => Ok("open", "Success");
}

[ApiController]
[Route("e2e/audit/protected")]
[Authorize]
public sealed class AuditProtectedController : ApiControllerBase
{
    [HttpPost]
    public ApiResult<string> Post() => Ok("protected", "Success");
}

[ApiController]
[Route("e2e/audit/admin-only")]
[Authorize(Roles = "admin")]
public sealed class AuditAdminOnlyController : ApiControllerBase
{
    [HttpPost]
    public ApiResult<string> Post() => Ok("admin-only", "Success");
}

[ApiController]
[Route("e2e/audit/links")]
public sealed class AuditLinksController : ApiControllerBase
{
    [HttpPost("{token}/accept")]
    public ApiResult<string> Accept(string token) => Ok("accepted", "Success");
}

[ApiController]
[Route("e2e/audit/disabled")]
[AuditDisabled]
public sealed class AuditDisabledController : ApiControllerBase
{
    [HttpPost]
    public ApiResult<string> Post() => Ok("disabled", "Success");
}
