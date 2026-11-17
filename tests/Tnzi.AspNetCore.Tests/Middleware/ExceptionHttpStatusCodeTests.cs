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

    /// <summary>请求本身读不了（客户端读体时走了、体超上限）是客户端的错：用异常自带的 4xx，不在 4xx 区间的当 400。</summary>
    [Theory]
    [InlineData(400, 400)]
    [InlineData(408, 408)]
    [InlineData(413, 413)]
    [InlineData(431, 431)]
    [InlineData(500, 400)]
    [InlineData(200, 400)]
    public void BadHttpRequestException_UsesItsOwnClientErrorStatus(int thrown, int expected)
    {
        Assert.Equal(expected, ExceptionHttpStatusCode.Resolve(new BadHttpRequestException("Unexpected end of request content.", thrown)));
    }

    /// <summary>
    /// 客户端在上传途中断开：Kestrel 抛的是 BadHttpRequestException 而不是取消，RequestAborted 已触发也一样。
    /// 内置处理器答它自己的 400，观测类中间件记的也是 400 —— 不是 500，也不另算 499。
    /// </summary>
    [Fact]
    public void WithContext_ABodyReadTheClientAbandoned_IsTheExceptionsOwnStatus()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };

        Assert.Equal(400, ExceptionHttpStatusCode.Resolve(context, new BadHttpRequestException("Unexpected end of request content.", 400)));
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
