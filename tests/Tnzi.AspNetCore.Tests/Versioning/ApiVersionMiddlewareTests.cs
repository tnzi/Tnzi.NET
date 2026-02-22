
namespace Tnzi.AspNetCore.Tests.Versioning;

/// <summary>
/// API版本中间件测试
/// </summary>
public class ApiVersionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenDisabled_ShouldNotSetVersion()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var apiVersionOptions = new ApiVersionOptions
        {
            Enabled = false
        };

        var aspNetCoreOptions = new AspNetCoreOptions
        {
            ApiVersion = apiVersionOptions
        };

        var optionsMonitor = Mock.Of<IOptionsMonitor<AspNetCoreOptions>>(x => x.CurrentValue == aspNetCoreOptions);
        var middleware = new ApiVersionMiddleware(next, optionsMonitor);

        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        Assert.False(context.Items.ContainsKey("ApiVersion"));
        Assert.False(context.Response.Headers.ContainsKey("X-Api-Version"));
    }

    [Fact]
    public async Task InvokeAsync_WhenEnabled_ShouldReadVersionFromHeader()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;

        var apiVersionOptions = new ApiVersionOptions
        {
            Enabled = true,
            ReaderType = ApiVersionReaderType.Header,
            HeaderName = "X-Api-Version",
            DefaultVersion = "v1",
            ReportVersion = true,
            VersionHeaderName = "X-Api-Version"
        };

        var aspNetCoreOptions = new AspNetCoreOptions
        {
            ApiVersion = apiVersionOptions
        };

        var optionsMonitor = Mock.Of<IOptionsMonitor<AspNetCoreOptions>>(x => x.CurrentValue == aspNetCoreOptions);
        var middleware = new ApiVersionMiddleware(next, optionsMonitor);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Version"] = "v2";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("v2", context.Items["ApiVersion"]);
        Assert.Equal("v2", context.Response.Headers["X-Api-Version"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_WhenEnabled_ShouldReadVersionFromQueryString()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;

        var apiVersionOptions = new ApiVersionOptions
        {
            Enabled = true,
            ReaderType = ApiVersionReaderType.QueryString,
            QueryStringName = "api-version",
            DefaultVersion = "v1",
            ReportVersion = true,
            VersionHeaderName = "X-Api-Version"
        };

        var aspNetCoreOptions = new AspNetCoreOptions
        {
            ApiVersion = apiVersionOptions
        };

        var optionsMonitor = Mock.Of<IOptionsMonitor<AspNetCoreOptions>>(x => x.CurrentValue == aspNetCoreOptions);
        var middleware = new ApiVersionMiddleware(next, optionsMonitor);

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?api-version=v3");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("v3", context.Items["ApiVersion"]);
        Assert.Equal("v3", context.Response.Headers["X-Api-Version"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_WhenEnabled_ShouldReadVersionFromUrlPath()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;

        var apiVersionOptions = new ApiVersionOptions
        {
            Enabled = true,
            ReaderType = ApiVersionReaderType.UrlPath,
            UrlParameterName = "version",
            DefaultVersion = "v1",
            ReportVersion = true,
            VersionHeaderName = "X-Api-Version"
        };

        var aspNetCoreOptions = new AspNetCoreOptions
        {
            ApiVersion = apiVersionOptions
        };

        var optionsMonitor = Mock.Of<IOptionsMonitor<AspNetCoreOptions>>(x => x.CurrentValue == aspNetCoreOptions);
        var middleware = new ApiVersionMiddleware(next, optionsMonitor);

        var context = new DefaultHttpContext();
        context.Request.Path = "/version/v4/users";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("v4", context.Items["ApiVersion"]);
        Assert.Equal("v4", context.Response.Headers["X-Api-Version"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_WhenVersionNotFound_ShouldUseDefaultVersion()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;

        var apiVersionOptions = new ApiVersionOptions
        {
            Enabled = true,
            ReaderType = ApiVersionReaderType.Header,
            HeaderName = "X-Api-Version",
            DefaultVersion = "v1",
            ReportVersion = true,
            VersionHeaderName = "X-Api-Version"
        };

        var aspNetCoreOptions = new AspNetCoreOptions
        {
            ApiVersion = apiVersionOptions
        };

        var optionsMonitor = Mock.Of<IOptionsMonitor<AspNetCoreOptions>>(x => x.CurrentValue == aspNetCoreOptions);
        var middleware = new ApiVersionMiddleware(next, optionsMonitor);

        var context = new DefaultHttpContext();
        // 不设置Header，应该使用默认版本

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("v1", context.Items["ApiVersion"]);
        Assert.Equal("v1", context.Response.Headers["X-Api-Version"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_WhenReportVersionFalse_ShouldNotSetResponseHeader()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;

        var apiVersionOptions = new ApiVersionOptions
        {
            Enabled = true,
            ReaderType = ApiVersionReaderType.Header,
            HeaderName = "X-Api-Version",
            DefaultVersion = "v1",
            ReportVersion = false,
            VersionHeaderName = "X-Api-Version"
        };

        var aspNetCoreOptions = new AspNetCoreOptions
        {
            ApiVersion = apiVersionOptions
        };

        var optionsMonitor = Mock.Of<IOptionsMonitor<AspNetCoreOptions>>(x => x.CurrentValue == aspNetCoreOptions);
        var middleware = new ApiVersionMiddleware(next, optionsMonitor);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Version"] = "v2";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal("v2", context.Items["ApiVersion"]);
        Assert.False(context.Response.Headers.ContainsKey("X-Api-Version"));
    }

}