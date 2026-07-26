
namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 异常处理中间件测试
/// </summary>
public class ExceptionHandlingMiddlewareTests
{
    private readonly Mock<ILogger<ExceptionHandlingMiddleware>> _loggerMock;
    private readonly Mock<IWebHostEnvironment> _environmentMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    public ExceptionHandlingMiddlewareTests()
    {
        _loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        _environmentMock = new Mock<IWebHostEnvironment>();
        _environmentMock.Setup(e => e.EnvironmentName).Returns(Environments.Production);
        _serviceProviderMock = new Mock<IServiceProvider>();

        // 设置 ILogger<T> 依赖，中间件构造函数在创建 handler 时需要
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(ILogger<ValidationExceptionHandler>)))
            .Returns(Mock.Of<ILogger<ValidationExceptionHandler>>());
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(ILogger<BusinessExceptionHandler>)))
            .Returns(Mock.Of<ILogger<BusinessExceptionHandler>>());
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(ILogger<InfrastructureExceptionHandler>)))
            .Returns(Mock.Of<ILogger<InfrastructureExceptionHandler>>());
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(ILogger<DefaultExceptionHandler>)))
            .Returns(Mock.Of<ILogger<DefaultExceptionHandler>>());
    }

    private HttpContext CreateHttpContext(IServiceProvider? requestServices = null)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = requestServices ?? _serviceProviderMock.Object;
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task InvokeAsync_WhenNoException_ShouldCallNext()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };

        var options = new ExceptionHandlingOptions();
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WhenBusinessException_ShouldReturn400()
    {
        // Arrange
        RequestDelegate next = _ => throw new BusinessException("Test error", ErrorCodes.VALIDATION_ERROR, 400);

        var options = new ExceptionHandlingOptions();
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        Assert.Contains("Test error", responseBody);
    }

    [Fact]
    public async Task InvokeAsync_WhenGenericException_ShouldReturn500()
    {
        // Arrange
        RequestDelegate next = _ => throw new Exception("Internal error");

        var options = new ExceptionHandlingOptions();
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        // 生产环境中，非框架异常应该返回通用错误消息，不暴露具体异常信息
        Assert.Contains("An error occurred while processing your request", responseBody);
        Assert.DoesNotContain("Internal error", responseBody);
    }

    [Fact]
    public async Task InvokeAsync_WhenDevelopmentEnvironment_ShouldIncludeExceptionDetails()
    {
        // Arrange
        _environmentMock.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        RequestDelegate next = _ => throw new Exception("Development error");

        var options = new ExceptionHandlingOptions();
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(500, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        // 开发环境应该包含异常详情（如StackTrace）
        Assert.Contains("Development error", responseBody);
    }

    [Fact]
    public async Task InvokeAsync_WhenProductionEnvironment_ShouldNotIncludeExceptionDetails()
    {
        // Arrange
        _environmentMock.Setup(e => e.EnvironmentName).Returns(Environments.Production);

        RequestDelegate next = _ => throw new Exception("Production error");

        var options = new ExceptionHandlingOptions();
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(500, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        // 生产环境不应该包含敏感信息（如具体的异常消息和StackTrace）
        Assert.DoesNotContain("Production error", responseBody);
        Assert.DoesNotContain("at ", responseBody);
        // 应该使用通用错误消息
        Assert.Contains("An error occurred while processing your request", responseBody);
    }

    [Fact]
    public async Task InvokeAsync_WhenValidationException_ShouldReturn400WithValidationErrors()
    {
        // Arrange
        var validationErrors = new Dictionary<string, string[]>
        {
            { "Email", new[] { "Email is required", "Email format is invalid" } },
            { "Password", new[] { "Password must be at least 8 characters" } }
        };
        RequestDelegate next = _ => throw new ValidationException("Validation failed", validationErrors);

        var options = new ExceptionHandlingOptions();
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        Assert.Contains("Validation failed", responseBody);
        Assert.Contains("Email", responseBody);
    }

    [Fact]
    public async Task InvokeAsync_WhenRateLimitException_ShouldReturn429WithRetryAfterHeader()
    {
        // Arrange
        RequestDelegate next = _ => throw new RateLimitException("Rate limit exceeded", 60);

        var options = new ExceptionHandlingOptions();
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(429, context.Response.StatusCode);
        Assert.Equal("60", context.Response.Headers["Retry-After"].ToString());
        Assert.Equal("application/json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        Assert.Contains("Rate limit exceeded", responseBody);
    }

    [Fact]
    public async Task InvokeAsync_WhenInfrastructureException_ShouldReturn503()
    {
        // Arrange
        RequestDelegate next = _ => throw new InfrastructureException("Database", "Connection failed", isRetryable: true);

        var options = new ExceptionHandlingOptions();
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        Assert.Contains("Connection failed", responseBody);
    }

    [Fact]
    public async Task InvokeAsync_WhenConfigurationExceptionInProduction_ShouldNotExposeDetails()
    {
        // Arrange
        _environmentMock.Setup(e => e.EnvironmentName).Returns(Environments.Production);

        RequestDelegate next = _ => throw new ConfigurationException("Invalid configuration");

        var options = new ExceptionHandlingOptions();
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(503, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        // 生产环境不应该暴露详细配置错误
        Assert.DoesNotContain("Invalid configuration", responseBody);
    }

    [Fact]
    public async Task InvokeAsync_WhenShowDetailsInDevelopmentFalse_ShouldNotIncludeDetails()
    {
        // Arrange
        _environmentMock.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        RequestDelegate next = _ => throw new Exception("Test error");

        var options = new ExceptionHandlingOptions
        {
            ShowDetailsInDevelopment = false
        };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(500, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        // 当ShowDetailsInDevelopment为false时，应该使用通用错误消息
        var jsonDoc = JsonDocument.Parse(responseBody);
        // TnziJsonDefaults.Options 使用 camelCase 命名策略
        Assert.True(jsonDoc.RootElement.TryGetProperty("message", out var messageElement));
        var message = messageElement.GetString();
        Assert.NotNull(message);
        // 应该不包含详细的异常信息
        Assert.DoesNotContain("Test error", responseBody);
    }

    [Fact]
    public async Task InvokeAsync_WhenIncludeRequestIdFalse_ShouldNotIncludeRequestId()
    {
        // Arrange
        RequestDelegate next = _ => throw new BusinessException("Test error", ErrorCodes.BUSINESS_ERROR, 400);

        var options = new ExceptionHandlingOptions
        {
            IncludeRequestId = false
        };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        var jsonDoc = JsonDocument.Parse(responseBody);
        // TnziJsonDefaults.Options 使用 camelCase + WhenWritingNull，null 属性不会出现
        Assert.False(jsonDoc.RootElement.TryGetProperty("requestId", out _));
    }

    [Fact]
    public async Task InvokeAsync_WhenIncludeRequestIdTrue_ShouldIncludeRequestId()
    {
        // Arrange
        _environmentMock.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        var exception = new BusinessException("Test error", ErrorCodes.BUSINESS_ERROR, 400);
        exception.ContextData = new Dictionary<string, object>
        {
            { "Key", "Value" }
        };
        RequestDelegate next = _ => throw exception;

        var options = new ExceptionHandlingOptions
        {
            IncludeRequestId = true,
            IncludeContextData = true
        };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        var jsonDoc = JsonDocument.Parse(responseBody);
        // TnziJsonDefaults.Options 使用 camelCase 命名策略
        Assert.True(jsonDoc.RootElement.TryGetProperty("requestId", out var requestIdElement));
        Assert.Equal(context.TraceIdentifier, requestIdElement.GetString());
    }

    [Fact]
    public async Task InvokeAsync_WhenIncludeContextData_ShouldIncludeContextData()
    {
        // Arrange
        _environmentMock.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        var exception = new BusinessException("Test error", ErrorCodes.BUSINESS_ERROR, 400);
        exception.ContextData = new Dictionary<string, object>
        {
            { "UserId", "123" },
            { "Action", "CreateUser" }
        };

        RequestDelegate next = _ => throw exception;

        var options = new ExceptionHandlingOptions
        {
            IncludeContextData = true
        };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        var jsonDoc = JsonDocument.Parse(responseBody);
        // TnziJsonDefaults.Options 使用 camelCase 命名策略
        Assert.True(jsonDoc.RootElement.TryGetProperty("contextData", out var contextDataElement));
        Assert.True(contextDataElement.TryGetProperty("UserId", out var userIdElement));
        Assert.Equal("123", userIdElement.GetString());
    }

    [Fact]
    public async Task InvokeAsync_WhenIncludeContextDataFalse_ShouldNotIncludeContextData()
    {
        // Arrange
        var exception = new BusinessException("Test error", ErrorCodes.BUSINESS_ERROR, 400);
        exception.ContextData = new Dictionary<string, object>
        {
            { "UserId", "123" }
        };

        RequestDelegate next = _ => throw exception;

        var options = new ExceptionHandlingOptions
        {
            IncludeContextData = false
        };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        var jsonDoc = JsonDocument.Parse(responseBody);
        Assert.False(jsonDoc.RootElement.TryGetProperty("contextData", out _));
    }

    [Fact]
    public async Task InvokeAsync_WhenInfrastructureExceptionWithRetryable_ShouldIncludeRetryInfo()
    {
        // Arrange
        _environmentMock.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        RequestDelegate next = _ => throw new InfrastructureException("Cache", "Cache unavailable", isRetryable: true);

        var options = new ExceptionHandlingOptions
        {
            IncludeRetryInfo = true
        };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        var jsonDoc = JsonDocument.Parse(responseBody);
        // TnziJsonDefaults.Options 使用 camelCase 命名策略
        Assert.True(jsonDoc.RootElement.TryGetProperty("isRetryable", out var isRetryableElement));
        Assert.True(isRetryableElement.GetBoolean());
    }

    [Fact]
    public async Task InvokeAsync_WhenCustomHandlerReturnsResult_ShouldUseCustomResult()
    {
        // Arrange
        var customResult = new ExceptionHandlingResult
        {
            StatusCode = 418,
            Message = "Custom handler message",
            ErrorCode = "CUSTOM_ERROR",
            ShouldContinueHandling = false
        };

        var handler = new TestBusinessExceptionHandler(customResult);
        var exception = new BusinessException("Original message", ErrorCodes.BUSINESS_ERROR, 400);
        RequestDelegate next = _ => throw exception;

        var options = new ExceptionHandlingOptions
        {
            CustomHandlers = new Dictionary<Type, Type>
            {
                { typeof(BusinessException), typeof(TestBusinessExceptionHandler) }
            }
        };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        _serviceProviderMock.Setup(sp => sp.GetService(typeof(TestBusinessExceptionHandler)))
            .Returns(handler);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(418, context.Response.StatusCode);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        Assert.Contains("Custom handler message", responseBody);
    }

    [Fact]
    public async Task InvokeAsync_WhenCustomHandlerReturnsContinueHandling_ShouldThrowException()
    {
        // Arrange
        var customResult = new ExceptionHandlingResult
        {
            ShouldContinueHandling = true
        };

        var handler = new TestBusinessExceptionHandler(customResult);
        var exception = new BusinessException("Test error", ErrorCodes.BUSINESS_ERROR, 400);
        RequestDelegate next = _ => throw exception;

        var options = new ExceptionHandlingOptions
        {
            CustomHandlers = new Dictionary<Type, Type>
            {
                { typeof(BusinessException), typeof(TestBusinessExceptionHandler) }
            }
        };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        _serviceProviderMock.Setup(sp => sp.GetService(typeof(TestBusinessExceptionHandler)))
            .Returns(handler);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object);

        var context = new DefaultHttpContext();

        // Act & Assert
        await Assert.ThrowsAsync<BusinessException>(() => middleware.InvokeAsync(context));
    }

    [Fact]
    public async Task InvokeAsync_WhenExceptionStatisticsEnabled_ShouldRecordException()
    {
        // Arrange
        var exceptionStatsMock = new Mock<IExceptionStatistics>();
        var exception = new BusinessException("Test error", ErrorCodes.BUSINESS_ERROR, 400);
        RequestDelegate next = _ => throw exception;

        var options = new ExceptionHandlingOptions
        {
            EnableMetrics = true
        };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object,
            exceptionStatsMock.Object);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        exceptionStatsMock.Verify(s => s.RecordException(
            It.IsAny<Exception>(),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WhenExceptionStatisticsNull_ShouldNotThrow()
    {
        // Arrange
        var exception = new BusinessException("Test error", ErrorCodes.BUSINESS_ERROR, 400);
        RequestDelegate next = _ => throw exception;

        var options = new ExceptionHandlingOptions();
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next,
            _loggerMock.Object,
            _environmentMock.Object,
            optionsMonitor,
            _serviceProviderMock.Object,
            null);

        var context = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(400, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WhenLogRequestBodyEnabled_CapturesBodyIntoErrorLog()
    {
        // Arrange
        RequestDelegate next = _ => throw new Exception("boom");

        var options = new ExceptionHandlingOptions { LogRequestBody = true };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object);

        var context = CreateHttpContext();
        var bodyBytes = "{\"token\":\"secret-payload-123\"}"u8.ToArray();
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = bodyBytes.Length;

        // Act
        await middleware.InvokeAsync(context);

        // Assert - the request body was captured into an error log entry
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("secret-payload-123")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WhenLogRequestBodyDisabled_DoesNotCaptureBody()
    {
        // Arrange
        RequestDelegate next = _ => throw new Exception("boom");

        var options = new ExceptionHandlingOptions { LogRequestBody = false };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object);

        var context = CreateHttpContext();
        var bodyBytes = "{\"token\":\"secret-payload-123\"}"u8.ToArray();
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = bodyBytes.Length;

        // Act
        await middleware.InvokeAsync(context);

        // Assert - no log entry ever contains the body (default: buffering off)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("secret-payload-123")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}

/// <summary>
/// 测试用的业务异常处理器
/// </summary>
internal class TestBusinessExceptionHandler : IExceptionHandler<BusinessException>
{
    private readonly ExceptionHandlingResult? _result;

    public TestBusinessExceptionHandler(ExceptionHandlingResult? result = null)
    {
        _result = result;
    }

    public Task<ExceptionHandlingResult?> HandleAsync(
        BusinessException exception,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_result);
    }
}