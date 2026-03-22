using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.AI.Mcp.Server;
using Tnzi.AI.Mcp.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests;

public class McpServerHttpSecurityMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WithoutApiKey_ReturnsUnauthorized()
    {
        var services = new ServiceCollection();
        using var serviceProvider = services.BuildServiceProvider();

        var security = new McpServerSecurityMiddleware(
            MsOptions.Create(new McpServerOptions
            {
                Enabled = true,
                Transport = "sse",
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
            MsOptions.Create(new McpServerOptions
            {
                Enabled = true,
                Transport = "sse",
                RequireAuthentication = true,
                AllowedApiKeys = ["secret"]
            }),
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
            MsOptions.Create(new McpServerOptions
            {
                Enabled = true,
                Transport = "sse",
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
            MsOptions.Create(new McpServerOptions
            {
                Enabled = true,
                Transport = "sse",
                RequireAuthentication = true,
                AllowedApiKeys = ["secret"]
            }),
            security);

        var context = new DefaultHttpContext();
        context.Request.Headers[McpServerSecurityMiddleware.ApiKeyHeaderName] = "secret";
        context.Request.Headers[McpServerSecurityMiddleware.TenantHeaderName] = "tenant-a";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        context.Items[McpServerSecurityMiddleware.TenantHeaderName].ShouldBe("tenant-a");
        called.ShouldBeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WithQueryFallback_CallsNextMiddleware()
    {
        var services = new ServiceCollection();
        using var serviceProvider = services.BuildServiceProvider();

        var security = new McpServerSecurityMiddleware(
            MsOptions.Create(new McpServerOptions
            {
                Enabled = true,
                Transport = "sse",
                RequireAuthentication = true,
                AllowedApiKeys = ["secret"],
                TenantIsolation = true
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
            MsOptions.Create(new McpServerOptions
            {
                Enabled = true,
                Transport = "sse",
                RequireAuthentication = true,
                AllowedApiKeys = ["secret"],
                TenantIsolation = true
            }),
            security);

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?apiKey=secret&tenantId=tenant-q");
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        context.Items[McpServerSecurityMiddleware.TenantHeaderName].ShouldBe("tenant-q");
        called.ShouldBeTrue();
    }
}
