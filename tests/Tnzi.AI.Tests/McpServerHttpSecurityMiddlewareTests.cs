using Microsoft.AspNetCore.Http;
using Tnzi.AI.Mcp.Server;
using Tnzi.AI.Mcp.Options;

namespace Tnzi.AI.Tests;

public class McpServerHttpSecurityMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WithoutApiKey_ReturnsUnauthorized()
    {
        var services = new ServiceCollection();
        using var serviceProvider = services.BuildServiceProvider();

        var security = new McpServerSecurityMiddleware(
            new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions
            {
                Enabled = true,
                RequireAuthentication = true,
                AllowedApiKeys = ["secret"]
            }),
            NullLogger<McpServerSecurityMiddleware>.Instance,
            serviceProvider);

        var called = false;
        RequestDelegate next = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = new McpServerHttpSecurityMiddleware(
            next,
            security);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task InvokeAsync_WithApiKey_CallsNextMiddleware()
    {
        var services = new ServiceCollection();
        using var serviceProvider = services.BuildServiceProvider();

        var security = new McpServerSecurityMiddleware(
            new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions
            {
                Enabled = true,
                RequireAuthentication = true,
                AllowedApiKeys = ["secret"]
            }),
            NullLogger<McpServerSecurityMiddleware>.Instance,
            serviceProvider);

        var called = false;
        RequestDelegate next = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = new McpServerHttpSecurityMiddleware(
            next,
            security);

        var context = new DefaultHttpContext();
        context.Request.Headers[McpServerSecurityMiddleware.ApiKeyHeaderName] = "secret";
        context.Request.Headers["X-Tenant-Id"] = "tenant-a";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        // 客户端自报的租户头不进 Items：下游没有任何东西该读到它
        context.Items.ContainsKey("X-Tenant-Id").ShouldBeFalse();
        context.Items[McpServerSecurityMiddleware.CallerHashItemKey].ShouldBe(security.BuildClientKey(context, "secret"));
        called.ShouldBeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WithQueryFallback_AuthenticatesButIgnoresQueryTenant()
    {
        var services = new ServiceCollection();
        using var serviceProvider = services.BuildServiceProvider();

        // AllowApiKeyInQuery is off by default (secure). This test exercises
        // the opt-in transitional compatibility path, so it must be explicitly enabled.
        var security = new McpServerSecurityMiddleware(
            new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions
            {
                Enabled = true,
                RequireAuthentication = true,
                AllowedApiKeys = ["secret"],
                AllowApiKeyInQuery = true
            }),
            NullLogger<McpServerSecurityMiddleware>.Instance,
            serviceProvider);

        var called = false;
        RequestDelegate next = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var middleware = new McpServerHttpSecurityMiddleware(
            next,
            security);

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?apiKey=secret&tenantId=tenant-q");
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        // 租户提取整条删除：query 与 header 都不再进入限流键或 Items
        context.Items.ContainsKey("X-Tenant-Id").ShouldBeFalse();
        called.ShouldBeTrue();
    }
    [Fact]
    public async Task InvokeAsync_RunScopedCredential_StoresCallerScopeInItems()
    {
        // 凭据校验完不能丢：下游 McpServerHost 按 Items 里的调用面过滤 tools/list、拒绝越界的 tools/call
        var credential = new RunScopedCredential
        {
            RunId = Guid.NewGuid(), AgentId = Guid.NewGuid(), TenantId = Guid.NewGuid(), AllowedToolNames = ["create_ticket"]
        };
        var validator = new Mock<IRunScopedCredentialValidator>();
        validator.Setup(v => v.ValidateAsync("tnzi-run_x", It.IsAny<CancellationToken>())).ReturnsAsync(credential);

        var services = new ServiceCollection();
        services.AddScoped(_ => validator.Object);
        using var serviceProvider = services.BuildServiceProvider();

        var options = new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions
        {
            Enabled = true, RequireAuthentication = true, AllowedApiKeys = ["secret"]
        });
        var security = new McpServerSecurityMiddleware(options, NullLogger<McpServerSecurityMiddleware>.Instance, serviceProvider);
        var middleware = new McpServerHttpSecurityMiddleware(_ => Task.CompletedTask, security);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer tnzi-run_x";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        var scope = context.Items[McpServerSecurityMiddleware.CallerScopeItemKey].ShouldBeOfType<McpCallerScope>();
        scope.IsRunScoped.ShouldBeTrue();
        scope.TenantId.ShouldBe(credential.TenantId);
        scope.AllowsTool("create_ticket").ShouldBeTrue();
        scope.AllowsTool("agent-b").ShouldBeFalse();
    }

    [Fact]
    public async Task InvokeAsync_StaticApiKey_StoresUnrestrictedScope()
    {
        var services = new ServiceCollection();
        using var serviceProvider = services.BuildServiceProvider();
        var options = new StaticOptionsMonitor<McpServerOptions>(new McpServerOptions
        {
            Enabled = true, RequireAuthentication = true, AllowedApiKeys = ["secret"]
        });
        var security = new McpServerSecurityMiddleware(options, NullLogger<McpServerSecurityMiddleware>.Instance, serviceProvider);
        var middleware = new McpServerHttpSecurityMiddleware(_ => Task.CompletedTask, security);

        var context = new DefaultHttpContext();
        context.Request.Headers[McpServerSecurityMiddleware.ApiKeyHeaderName] = "secret";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        var scope = context.Items[McpServerSecurityMiddleware.CallerScopeItemKey].ShouldBeOfType<McpCallerScope>();
        scope.IsRunScoped.ShouldBeFalse();
        scope.AllowsTool("anything").ShouldBeTrue();
    }
}
