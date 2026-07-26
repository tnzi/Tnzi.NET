
using Tnzi.AspNetCore.Options;
using Tnzi.MultiTenancy;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// TenantResolverMiddleware 单元测试
/// </summary>
public class TenantResolverMiddlewareTests
{
    private readonly Mock<ILogger<TenantResolverMiddleware>> _loggerMock;
    private readonly Mock<ICurrentTenant> _currentTenantMock;
    private readonly Mock<ITenantChecker> _tenantCheckerMock;

    public TenantResolverMiddlewareTests()
    {
        _loggerMock = new Mock<ILogger<TenantResolverMiddleware>>();
        _currentTenantMock = new Mock<ICurrentTenant>();
        _tenantCheckerMock = new Mock<ITenantChecker>();

        // 默认：Change 返回一个可 Dispose 的作用域
        _currentTenantMock
            .Setup(c => c.Change(It.IsAny<Guid?>(), It.IsAny<string?>()))
            .Returns(new NoopDisposable());
    }

    private static IOptions<TenantResolverOptions> CreateOptions(Action<TenantResolverOptions>? configure = null)
    {
        var options = new TenantResolverOptions();
        configure?.Invoke(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    private static DefaultHttpContext CreateHttpContextWithBody()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task InvalidTenant_ReturnsJsonErrorResponse()
    {
        // Arrange
        var tenantId = Guid.NewGuid();

        // 租户检查器返回 false（无效/未激活租户）
        _tenantCheckerMock
            .Setup(c => c.IsActiveAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = new TenantResolverMiddleware(
            next,
            CreateOptions(),
            _loggerMock.Object,
            _tenantCheckerMock.Object);

        var context = CreateHttpContextWithBody();
        // 在 Header 中传入租户 ID
        context.Request.Headers["X-Tenant-Id"] = tenantId.ToString();

        // Act
        await middleware.InvokeAsync(context, _currentTenantMock.Object);

        // Assert - 应返回 400
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);

        // next 不应被调用
        Assert.False(nextCalled);

        // 响应体应包含错误信息
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();

        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("succeeded", out var succeededEl));
        Assert.False(succeededEl.GetBoolean());

        Assert.True(root.TryGetProperty("code", out var codeEl));
        Assert.Equal(400, codeEl.GetInt32());

        Assert.True(root.TryGetProperty("message", out var messageEl));
        Assert.False(string.IsNullOrWhiteSpace(messageEl.GetString()));

        Assert.True(root.TryGetProperty("error", out var errorEl));
        Assert.Equal("INVALID_TENANT", errorEl.GetString());
    }

    [Fact]
    public async Task ValidTenant_PassesThrough()
    {
        // Arrange
        var tenantId = Guid.NewGuid();

        // 租户检查器返回 true（有效租户）
        _tenantCheckerMock
            .Setup(c => c.IsActiveAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = new TenantResolverMiddleware(
            next,
            CreateOptions(),
            _loggerMock.Object,
            _tenantCheckerMock.Object);

        var context = CreateHttpContextWithBody();
        context.Request.Headers["X-Tenant-Id"] = tenantId.ToString();

        // Act
        await middleware.InvokeAsync(context, _currentTenantMock.Object);

        // Assert - 应正常通过并切换租户上下文
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);

        _currentTenantMock.Verify(
            c => c.Change(tenantId, It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task NoTenantHeader_PassesThrough()
    {
        // Arrange - 请求中没有任何租户标识，也没有默认租户 ID
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = new TenantResolverMiddleware(
            next,
            CreateOptions(o => o.DefaultTenantId = null),
            _loggerMock.Object,
            tenantChecker: null);  // 没有租户检查器

        var context = CreateHttpContextWithBody();
        // 不设置任何租户 Header

        // Act
        await middleware.InvokeAsync(context, _currentTenantMock.Object);

        // Assert - 应直接传递给下一个中间件，不切换租户
        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);

        // 不应切换租户上下文
        _currentTenantMock.Verify(
            c => c.Change(It.IsAny<Guid?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task NoTenantChecker_ValidHeader_PassesThrough()
    {
        // Arrange - 没有 ITenantChecker，即便提供了有效 Header 也应直接通过
        var tenantId = Guid.NewGuid();

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var middleware = new TenantResolverMiddleware(
            next,
            CreateOptions(),
            _loggerMock.Object,
            tenantChecker: null);  // 不注册租户检查器

        var context = CreateHttpContextWithBody();
        context.Request.Headers["X-Tenant-Id"] = tenantId.ToString();

        // Act
        await middleware.InvokeAsync(context, _currentTenantMock.Object);

        // Assert - 没有检查器时跳过验证，直接通过并切换租户
        Assert.True(nextCalled);
        _currentTenantMock.Verify(c => c.Change(tenantId, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task InvalidTenant_LogsWarning()
    {
        // Arrange
        var tenantId = Guid.NewGuid();

        _tenantCheckerMock
            .Setup(c => c.IsActiveAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        RequestDelegate next = _ => Task.CompletedTask;

        var middleware = new TenantResolverMiddleware(
            next,
            CreateOptions(),
            _loggerMock.Object,
            _tenantCheckerMock.Object);

        var context = CreateHttpContextWithBody();
        context.Request.Headers["X-Tenant-Id"] = tenantId.ToString();

        // Act
        await middleware.InvokeAsync(context, _currentTenantMock.Object);

        // Assert - 应记录 Warning 日志
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("invalid") || state.ToString()!.Contains("inactive")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// 空操作 IDisposable，用于 Mock ICurrentTenant.Change 的返回值
    /// </summary>
    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
