using System.Text;


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
        var bodyBytes = "{\"note\":\"secret-payload-123\"}"u8.ToArray();
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
        var bodyBytes = "{\"note\":\"secret-payload-123\"}"u8.ToArray();
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

    [Theory]
    [InlineData("multipart/form-data; boundary=----x", 64L)]
    [InlineData("application/octet-stream", 64L)]
    [InlineData("application/json", 8L * 1024 + 1)]
    [InlineData("application/json", null)]
    public async Task InvokeAsync_ABodyTheGateRefuses_IsNeverBufferedNorLogged(string contentType, long? contentLength)
    {
        // 缓冲只能在下游读体之前开，而 EnableBuffering 会把整条体复制一份（超过 30 KB 落盘）。
        // 开着 LogRequestBody 时此前对每个请求无条件开：只为出错时看 8 KB，每次 multipart 上传都被整条落盘。
        // 文件上传 / 二进制 / 超过上界 / 没有 Content-Length（读发生在消费之后，事后设不了上界）的体
        // 都不缓冲 —— 它们本来就不会被记进日志。
        RequestDelegate next = _ => throw new Exception("boom");
        var options = new ExceptionHandlingOptions { LogRequestBody = true };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);
        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object);

        var context = CreateHttpContext();
        var original = new ForwardOnlyStream("{\"note\":\"secret-payload-123\"}"u8.ToArray());
        context.Request.Method = "POST";
        context.Request.ContentType = contentType;
        context.Request.ContentLength = contentLength;
        context.Request.Body = original;

        await middleware.InvokeAsync(context);

        // 不可定位的流被 EnableBuffering 换掉即等于整条落盘一份；没被换掉才是「没缓冲」。
        Assert.Same(original, context.Request.Body);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("secret-payload-123")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Theory]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task InvokeAsync_ATextBodyUnderTheCap_IsBufferedAndCapturedFromAForwardOnlyStream(string contentType)
    {
        // 防锈：闸门只挡该挡的。Kestrel 给的体是不可定位的，文本型且没超上界的照旧缓冲并记进日志。
        RequestDelegate next = async ctx =>
        {
            // 下游先把体消费掉（模型绑定就是这样），异常之后仍要能回读。
            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            await reader.ReadToEndAsync();
            throw new Exception("boom");
        };
        var options = new ExceptionHandlingOptions { LogRequestBody = true };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);
        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object);

        var context = CreateHttpContext();
        var bodyBytes = "{\"note\":\"secret-payload-123\"}"u8.ToArray();
        context.Request.Method = "POST";
        context.Request.ContentType = contentType;
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.Body = new ForwardOnlyStream(bodyBytes);

        await middleware.InvokeAsync(context);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("secret-payload-123")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData("application/json", "{\"userName\":\"alice\",\"password\":\"hunter2-plaintext\"}")]
    [InlineData("application/x-www-form-urlencoded", "userName=alice&password=hunter2-plaintext")]
    public async Task InvokeAsync_CapturedBody_IsRedactedBeforeItReachesTheLog(string contentType, string body)
    {
        // 这个开关可以热开，异常最常见的现场正是登录、改密这类带凭据的请求：记下来的体必须先脱敏。
        RequestDelegate next = _ => throw new Exception("boom");
        var options = new ExceptionHandlingOptions { LogRequestBody = true };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);
        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object);

        var context = CreateHttpContext();
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        context.Request.Method = "POST";
        context.Request.Path = "/api/account/profile";
        context.Request.ContentType = contentType;
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.Body = new MemoryStream(bodyBytes);

        await middleware.InvokeAsync(context);

        // 体确实被记了（非敏感字段还在），只是密码不在里面。
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("alice")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("hunter2-plaintext")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/connect/token")]
    public async Task InvokeAsync_AuthenticationEndpoints_AreNeverCaptured(string path)
    {
        // 认证端点上「哪个字段是凭据」按字段名猜不全（嵌套细节对象、自定义字段名），与请求日志一样整条不采。
        RequestDelegate next = _ => throw new Exception("boom");
        var options = new ExceptionHandlingOptions { LogRequestBody = true };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);
        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object);

        var context = CreateHttpContext();
        var bodyBytes = "{\"note\":\"secret-payload-123\"}"u8.ToArray();
        var original = new ForwardOnlyStream(bodyBytes);
        context.Request.Method = "POST";
        context.Request.Path = path;
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.Body = original;

        await middleware.InvokeAsync(context);

        Assert.Same(original, context.Request.Body);
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o.ToString()!.Contains("secret-payload-123")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    /// <summary>不可定位的请求体：Kestrel 给的就是这种。被 EnableBuffering 换掉即等于整条落盘一份。</summary>
    private sealed class ForwardOnlyStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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