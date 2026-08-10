namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 请求校验中间件：只对<b>自己的校验</b>负责，下游异常必须原样上抛。
/// </summary>
/// <remarks>
/// <para>
/// 锁的是一条会架空整个异常处理契约的写法：<c>await _next(context)</c> 曾经写在 try 内部，
/// 而 catch 捕获一切并返回 500 <c>"Request validation error"</c>。
/// <c>ExceptionHandlingMiddleware</c> 注册在**外层**（管线更早），异常在这里就被吃掉，它一次都轮不到。
/// </para>
/// <para>
/// 后果：<c>NotFoundException</c> 该 404、<c>ValidationException</c> 该 400 带 errorDetails、
/// <c>ForbiddenException</c> 该 403，统统变成 <c>{code:500}</c>。前端按 <c>body.code</c> 分支的逻辑
/// （401 触发刷新令牌、404 走空态）随之失效；服务端日志把业务异常记成「验证中间件出错」，
/// 排查方向被带偏。开关是 <c>AspNetCore:RequestValidation:Enabled</c>，打开即全站生效。
/// </para>
/// </remarks>
public class RequestValidationMiddlewareTests
{
    private static RequestValidationMiddleware Create(RequestDelegate next, IRequestValidator validator)
        => new(next, validator, Mock.Of<ILogger<RequestValidationMiddleware>>());

    private static IRequestValidator PassingValidator()
    {
        var mock = new Mock<IRequestValidator>();
        mock.Setup(v => v.ValidateAsync(It.IsAny<HttpContext>())).ReturnsAsync((string?)null);
        return mock.Object;
    }

    [Fact]
    public async Task DownstreamException_IsNotSwallowed()
    {
        var middleware = Create(
            _ => throw new InvalidOperationException("downstream blew up"),
            PassingValidator());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(new DefaultHttpContext()));

        Assert.Equal("downstream blew up", ex.Message);
    }

    /// <summary>下游异常不得被改写成 500 响应体 —— 那会绕过外层的异常处理中间件。</summary>
    [Fact]
    public async Task DownstreamException_DoesNotWriteA500Body()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = Create(
            _ => throw new InvalidOperationException("downstream blew up"),
            PassingValidator());

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Empty(body);
    }

    /// <summary>校验器<b>自己</b>出错仍然拒绝请求：校验没跑完等于这个请求没被检查过。</summary>
    [Fact]
    public async Task ValidatorFailure_StillRejectsWith500()
    {
        var nextCalled = false;
        var validator = new Mock<IRequestValidator>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<HttpContext>()))
            .ThrowsAsync(new InvalidOperationException("validator broke"));

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = Create(_ => { nextCalled = true; return Task.CompletedTask; }, validator.Object);

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.False(nextCalled, "校验器失效时放行，等于让失效变成静默放行");
    }

    [Fact]
    public async Task ValidationError_RejectsWith400AndDoesNotCallNext()
    {
        var nextCalled = false;
        var validator = new Mock<IRequestValidator>();
        validator.Setup(v => v.ValidateAsync(It.IsAny<HttpContext>())).ReturnsAsync("bad request shape");

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = Create(_ => { nextCalled = true; return Task.CompletedTask; }, validator.Object);

        await middleware.InvokeAsync(context);

        Assert.Equal(400, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task ValidationPasses_CallsNext()
    {
        var nextCalled = false;
        var middleware = Create(_ => { nextCalled = true; return Task.CompletedTask; }, PassingValidator());

        await middleware.InvokeAsync(new DefaultHttpContext());

        Assert.True(nextCalled);
    }
}
