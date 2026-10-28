using System.Text;
using Microsoft.AspNetCore.Http.Features;


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
    public async Task InvokeAsync_WhenCustomHandlerReturnsContinueHandling_FallsThroughToTheBuiltInChain()
    {
        // 「继续使用默认处理逻辑」= 交给内置处理器链出标准信封；此前是 throw; 把异常抛出最外层中间件，
        // 客户端拿到的是服务器裸 500，没有信封。
        var handler = new TestBusinessExceptionHandler(new ExceptionHandlingResult { ShouldContinueHandling = true });
        RequestDelegate next = _ => throw new BusinessException("Test error", ErrorCodes.BUSINESS_ERROR, 409);

        var options = new ExceptionHandlingOptions
        {
            CustomHandlers = new Dictionary<Type, Type>
            {
                { typeof(BusinessException), typeof(TestBusinessExceptionHandler) }
            }
        };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(TestBusinessExceptionHandler))).Returns(handler);

        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object);
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(409, context.Response.StatusCode);
        var body = await ReadBodyAsync(context);
        Assert.Equal("Test error", body.RootElement.GetProperty("message").GetString());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task InvokeAsync_ACustomHandlerRegisteredForABaseType_HandlesItsSubclasses()
    {
        // 按精确类型查表时，登记在 BusinessException 上的处理器永远看不到 ResourceNotFoundException。
        var handler = new TestBusinessExceptionHandler(new ExceptionHandlingResult { StatusCode = 418, Message = "From base handler" });
        RequestDelegate next = _ => throw new ResourceNotFoundException("Missing");

        var options = new ExceptionHandlingOptions
        {
            CustomHandlers = new Dictionary<Type, Type>
            {
                { typeof(BusinessException), typeof(TestBusinessExceptionHandler) }
            }
        };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(TestBusinessExceptionHandler))).Returns(handler);

        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object);
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(418, context.Response.StatusCode);
        Assert.Equal("From base handler", (await ReadBodyAsync(context)).RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task InvokeAsync_TheNearestRegisteredTypeWins()
    {
        var baseHandler = new TestBusinessExceptionHandler(new ExceptionHandlingResult { StatusCode = 418, Message = "base" });
        var exactHandler = new TestNotFoundExceptionHandler(new ExceptionHandlingResult { StatusCode = 410, Message = "exact" });
        RequestDelegate next = _ => throw new ResourceNotFoundException("Missing");

        var options = new ExceptionHandlingOptions
        {
            CustomHandlers = new Dictionary<Type, Type>
            {
                { typeof(BusinessException), typeof(TestBusinessExceptionHandler) },
                { typeof(ResourceNotFoundException), typeof(TestNotFoundExceptionHandler) }
            }
        };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(TestBusinessExceptionHandler))).Returns(baseHandler);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(TestNotFoundExceptionHandler))).Returns(exactHandler);

        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object);
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(410, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WhenIncludeRetryInfoFalse_OmitsIsRetryableAndKeepsTheStandardEnvelope()
    {
        RequestDelegate next = _ => throw new InfrastructureException("Cache", "Cache unavailable", isRetryable: true);
        var options = new ExceptionHandlingOptions { IncludeRetryInfo = false };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);
        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object);
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(503, context.Response.StatusCode);
        var body = await ReadBodyAsync(context);
        Assert.False(body.RootElement.TryGetProperty("isRetryable", out _));
        Assert.False(body.RootElement.TryGetProperty("contextData", out _));
    }

    [Fact]
    public async Task InvokeAsync_TheRequestIdTheClientHolds_IsWhatTheErrorPathRecords()
    {
        // 客户端在 X-Request-Id 响应头里拿到的是追踪中间件给的 id；错误路径此前记的是 Kestrel 的 TraceIdentifier，
        // 标准信封里则根本没有 requestId，三处对不上。
        var stats = new Mock<IExceptionStatistics>();
        RequestDelegate next = _ => throw new ResourceNotFoundException("Missing");
        var options = new ExceptionHandlingOptions { IncludeRequestId = true };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);
        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object, stats.Object);
        var context = CreateHttpContext();
        context.TraceIdentifier = "0HN-connection:00000002";
        context.Items["RequestId"] = "client-abc";

        await middleware.InvokeAsync(context);

        stats.Verify(s => s.RecordException(It.IsAny<Exception>(), "client-abc"), Times.Once);
        var body = await ReadBodyAsync(context);
        Assert.Equal(404, body.RootElement.GetProperty("code").GetInt32());
        Assert.Equal("client-abc", body.RootElement.GetProperty("requestId").GetString());
    }

    [Fact]
    public async Task InvokeAsync_WhenIncludeRequestIdFalse_TheStandardEnvelopeCarriesNoRequestId()
    {
        RequestDelegate next = _ => throw new ResourceNotFoundException("Missing");
        var options = new ExceptionHandlingOptions { IncludeRequestId = false };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);
        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object);
        var context = CreateHttpContext();
        context.Items["RequestId"] = "client-abc";

        await middleware.InvokeAsync(context);

        Assert.False((await ReadBodyAsync(context)).RootElement.TryGetProperty("requestId", out _));
    }

    [Fact]
    public async Task InvokeAsync_AClientAbort_IsNotAServerError()
    {
        // 用户关掉页面：EF / HttpClient 抛 TaskCanceledException。此前被记成 Error 级系统异常、计入异常统计、
        // 再往已断开的连接写一个 500。
        var stats = new Mock<IExceptionStatistics>();
        var handlerLogger = new Mock<ILogger<DefaultExceptionHandler>>();
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(ILogger<DefaultExceptionHandler>))).Returns(handlerLogger.Object);
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        RequestDelegate next = _ => throw new TaskCanceledException();

        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == new ExceptionHandlingOptions());
        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object, stats.Object);
        var context = CreateHttpContext();
        context.RequestAborted = aborted.Token;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status499ClientClosedRequest, context.Response.StatusCode);
        stats.Verify(s => s.RecordException(It.IsAny<Exception>(), It.IsAny<string?>()), Times.Never);
        VerifyNoErrorLogged(handlerLogger);
        VerifyNoErrorLogged(_loggerMock);
    }

    [Fact]
    public async Task InvokeAsync_ACancellationTheClientDidNotCause_IsStillAServerError()
    {
        // 过宽的吞法会把内部超时（未开 RequestAborted 的取消）也当成客户端断开。
        var stats = new Mock<IExceptionStatistics>();
        RequestDelegate next = _ => throw new TaskCanceledException("internal timeout");

        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == new ExceptionHandlingOptions());
        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object, stats.Object);
        var context = CreateHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        stats.Verify(s => s.RecordException(It.IsAny<Exception>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_AnExceptionAfterTheResponseStarted_IsRethrownSoTheServerAbortsTheResponse()
    {
        // 流式响应写过一段后抛异常：此前中间件只记一行不带堆栈的 Warning 然后正常返回，
        // Kestrel 于是写出结束块，客户端看到一个 200、看上去完整、其实被截断的响应体。
        var stats = new Mock<IExceptionStatistics>();
        var failure = new InvalidOperationException("mid-stream failure");
        RequestDelegate next = _ => throw failure;

        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == new ExceptionHandlingOptions());
        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object, stats.Object);
        var context = CreateHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Same(failure, thrown);
        stats.Verify(s => s.RecordException(failure, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_AnExceptionAfterTheResponseStarted_WhenTheClientIsGone_IsNotRethrown()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        RequestDelegate next = _ => throw new IOException("connection reset");

        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == new ExceptionHandlingOptions());
        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object);
        var context = CreateHttpContext();
        context.RequestAborted = aborted.Token;
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        await middleware.InvokeAsync(context);

        VerifyNoErrorLogged(_loggerMock);
    }

    private static async Task<JsonDocument> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return JsonDocument.Parse(await reader.ReadToEndAsync());
    }

    private static void VerifyNoErrorLogged<T>(Mock<ILogger<T>> logger)
        => logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);

    /// <summary>响应头已经发出（流式端点写过第一块）。</summary>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
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
                It.Is<It.IsAnyType>((o, _) => o != null && o.ToString()!.Contains("secret-payload-123")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WhenLogRequestBodyEnabled_RedactsACredentialInThePath()
    {
        // 与请求日志同一道脱敏：签署页的 signing/{token} 是外部签署人的全部身份，
        // 异常诊断把路径写进服务端日志时不能把它带出来。
        const string secret = "S3CR3T-SIGNING-TOKEN";
        RequestDelegate next = ctx =>
        {
            ctx.Request.RouteValues["token"] = secret;
            throw new Exception("boom");
        };

        var options = new ExceptionHandlingOptions { LogRequestBody = true };
        var optionsMonitor = Mock.Of<IOptionsMonitor<ExceptionHandlingOptions>>(x => x.CurrentValue == options);

        var middleware = new ExceptionHandlingMiddleware(
            next, _loggerMock.Object, _environmentMock.Object, optionsMonitor, _serviceProviderMock.Object);

        var context = CreateHttpContext();
        var bodyBytes = "{\"fieldValues\":{\"name\":\"x\"}}"u8.ToArray();
        context.Request.Method = "POST";
        context.Request.Path = "/api/signing/" + secret;
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = bodyBytes.Length;

        await middleware.InvokeAsync(context);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o != null && o.ToString()!.Contains("/api/signing/***")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o != null && o.ToString()!.Contains(secret)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
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
                It.Is<It.IsAnyType>((o, _) => o != null && o.ToString()!.Contains("secret-payload-123")),
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
                It.Is<It.IsAnyType>((o, _) => o != null && o.ToString()!.Contains("secret-payload-123")),
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
                It.Is<It.IsAnyType>((o, _) => o != null && o.ToString()!.Contains("secret-payload-123")),
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
                It.Is<It.IsAnyType>((o, _) => o != null && o.ToString()!.Contains("alice")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) => o != null && o.ToString()!.Contains("hunter2-plaintext")),
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
                It.Is<It.IsAnyType>((o, _) => o != null && o.ToString()!.Contains("secret-payload-123")),
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

/// <summary>
/// 测试用的、登记在子类上的处理器（验证「最近的登记类型优先」）
/// </summary>
internal class TestNotFoundExceptionHandler(ExceptionHandlingResult? result) : IExceptionHandler<ResourceNotFoundException>
{
    public Task<ExceptionHandlingResult?> HandleAsync(
        ResourceNotFoundException exception,
        HttpContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(result);
}