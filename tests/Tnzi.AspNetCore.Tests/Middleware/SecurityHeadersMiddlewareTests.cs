
namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 安全头部中间件测试
/// </summary>
public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenEnabled_ShouldAddSecurityHeaders()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var securityHeadersOptions = new SecurityHeadersOptions
        {
            EnableSecurityHeaders = true,
            ContentSecurityPolicy = "default-src 'self'",
            XFrameOptions = "DENY",
            XContentTypeOptions = "nosniff",
            XXssProtection = "1; mode=block",
            ReferrerPolicy = "strict-origin-when-cross-origin"
        };

        var aspNetCoreOptions = new AspNetCoreOptions
        {
            SecurityHeaders = securityHeadersOptions
        };

        var optionsMonitor = Mock.Of<IOptionsMonitor<AspNetCoreOptions>>(x => x.CurrentValue == aspNetCoreOptions);
        var middleware = new SecurityHeadersMiddleware(next, optionsMonitor);

        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        Assert.Equal("default-src 'self'", context.Response.Headers["Content-Security-Policy"].ToString());
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("1; mode=block", context.Response.Headers["X-XSS-Protection"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_WhenDisabled_ShouldNotAddSecurityHeaders()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var securityHeadersOptions = new SecurityHeadersOptions
        {
            EnableSecurityHeaders = false
        };

        var aspNetCoreOptions = new AspNetCoreOptions
        {
            SecurityHeaders = securityHeadersOptions
        };

        var optionsMonitor = Mock.Of<IOptionsMonitor<AspNetCoreOptions>>(x => x.CurrentValue == aspNetCoreOptions);
        var middleware = new SecurityHeadersMiddleware(next, optionsMonitor);

        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        Assert.False(context.Response.Headers.ContainsKey("Content-Security-Policy"));
        Assert.False(context.Response.Headers.ContainsKey("X-Frame-Options"));
    }

    [Fact]
    public async Task InvokeAsync_WhenHttps_ShouldAddHstsHeader()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;

        var securityHeadersOptions = new SecurityHeadersOptions
        {
            EnableSecurityHeaders = true,
            HstsEnabled = true,
            HstsMaxAge = 31536000,
            HstsIncludeSubDomains = true,
            HstsPreload = true
        };

        var aspNetCoreOptions = new AspNetCoreOptions
        {
            SecurityHeaders = securityHeadersOptions
        };

        var optionsMonitor = Mock.Of<IOptionsMonitor<AspNetCoreOptions>>(x => x.CurrentValue == aspNetCoreOptions);
        var middleware = new SecurityHeadersMiddleware(next, optionsMonitor);

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var hstsHeader = context.Response.Headers["Strict-Transport-Security"].ToString();
        Assert.Contains("max-age=31536000", hstsHeader);
        Assert.Contains("includeSubDomains", hstsHeader);
        Assert.Contains("preload", hstsHeader);
    }

    [Fact]
    public async Task InvokeAsync_WhenHttp_ShouldNotAddHstsHeader()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;

        var securityHeadersOptions = new SecurityHeadersOptions
        {
            EnableSecurityHeaders = true,
            HstsEnabled = true
        };

        var aspNetCoreOptions = new AspNetCoreOptions
        {
            SecurityHeaders = securityHeadersOptions
        };

        var optionsMonitor = Mock.Of<IOptionsMonitor<AspNetCoreOptions>>(x => x.CurrentValue == aspNetCoreOptions);
        var middleware = new SecurityHeadersMiddleware(next, optionsMonitor);

        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(context.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }

    [Fact]
    public async Task InvokeAsync_WhenSecurityHeadersIsNull_ShouldCallNext()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var aspNetCoreOptions = new AspNetCoreOptions
        {
            SecurityHeaders = null
        };

        var optionsMonitor = Mock.Of<IOptionsMonitor<AspNetCoreOptions>>(x => x.CurrentValue == aspNetCoreOptions);
        var middleware = new SecurityHeadersMiddleware(next, optionsMonitor);

        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
    }
}