using Microsoft.AspNetCore.Http.Features;

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

    [Fact]
    public void WithContext_ClientAbortIs499()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };

        Assert.Equal(499, ExceptionHttpStatusCode.Resolve(context, new OperationCanceledException(aborted.Token)));
    }

    /// <summary>不是客户端断开引起的取消（内部超时等）仍是服务器错误。</summary>
    [Fact]
    public void WithContext_CancellationWithoutClientAbortIs500()
    {
        Assert.Equal(500, ExceptionHttpStatusCode.Resolve(new DefaultHttpContext(), new OperationCanceledException()));
    }

    [Fact]
    public void WithContext_StartedResponseKeepsTheCodeAlreadySent()
    {
        var context = new DefaultHttpContext();
        var responseFeature = new StartedResponseFeature { StatusCode = 200 };
        context.Features.Set<IHttpResponseFeature>(responseFeature);

        Assert.Equal(200, ExceptionHttpStatusCode.Resolve(context, new InvalidOperationException("boom")));
    }

    [Fact]
    public void WithContext_OtherwiseFollowsTheExceptionTable()
    {
        Assert.Equal(404, ExceptionHttpStatusCode.Resolve(new DefaultHttpContext(), new ResourceNotFoundException("Order", 1)));
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
