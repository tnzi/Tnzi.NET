namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// <see cref="ExceptionHttpStatusCode"/> 与内置异常处理器同一张表：观测类中间件在响应写出之前按它折算客户端将收到的码。
/// </summary>
public class ExceptionHttpStatusCodeTests
{
    [Fact]
    public void BusinessException_UsesItsOwnStatusCode()
    {
        Assert.Equal(403, ExceptionHttpStatusCode.Resolve(new ForbiddenException()));
        Assert.Equal(404, ExceptionHttpStatusCode.Resolve(new ResourceNotFoundException("Order", 1)));
        Assert.Equal(401, ExceptionHttpStatusCode.Resolve(new UnauthorizedException()));
    }

    [Fact]
    public void InfrastructureException_Is503()
    {
        Assert.Equal(503, ExceptionHttpStatusCode.Resolve(new ConfigurationException("Missing setting")));
    }

    [Fact]
    public void AnythingElse_Is500()
    {
        Assert.Equal(500, ExceptionHttpStatusCode.Resolve(new InvalidOperationException("boom")));
        Assert.Equal(500, ExceptionHttpStatusCode.Resolve(new TnziException("FRAMEWORK_ERROR", "framework")));
    }

    [Fact]
    public void Null_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => ExceptionHttpStatusCode.Resolve(null!));
    }
}
