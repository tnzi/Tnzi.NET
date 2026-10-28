namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// 一个正在向上抛的异常最终会变成哪个 HTTP 状态码：与 <see cref="ExceptionHandlingMiddleware"/> 的内置处理器同一口径。
/// </summary>
/// <remarks>
/// 挂在异常处理中间件<b>之内</b>的观测类中间件（访问日志、性能采样）看到异常时响应还没写、
/// <c>Response.StatusCode</c> 仍是 200；它们要记下客户端实际会收到的码，就得按同一张表折算，
/// 而不是一律按 500 —— 服务层抛的 <c>ForbiddenException</c> / <c>NotFoundException</c> 是 403 / 404，
/// 记成 500 会让「服务器错误」的统计被业务拒绝灌满。
/// 消费方自定义的 <c>IExceptionHandler</c> 不在这张表里；那是刻意的：这里只承诺内置口径。
/// </remarks>
public static class ExceptionHttpStatusCode
{
    /// <summary>按内置处理器的口径折算 <paramref name="exception"/> 对应的状态码。</summary>
    public static int Resolve(Exception exception)
    {
        Check.NotNull(exception);

        return exception switch
        {
            BusinessException businessException => businessException.HttpStatusCode,
            InfrastructureException => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };
    }

    /// <summary>
    /// 一个从下游抛到观测类中间件的异常，客户端最终会收到哪个状态码：响应已开始就是已经发出的那个；
    /// 客户端自己断开（<c>RequestAborted</c> 已触发的取消）是 499，与异常处理中间件的处置一致；
    /// 其余按 <see cref="Resolve(Exception)"/> 折算。
    /// </summary>
    /// <remarks>
    /// 只用 <see cref="Resolve(Exception)"/> 的话，关页面、导航走引起的取消会被记成 500，
    /// 「服务器错误」的统计里混进客户端行为；而响应已开始后抛出的异常，客户端收到的仍是已发出的那个码。
    /// </remarks>
    public static int Resolve(HttpContext context, Exception exception)
    {
        Check.NotNull(context);
        Check.NotNull(exception);

        if (context.Response.HasStarted)
        {
            return context.Response.StatusCode;
        }

        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            return StatusCodes.Status499ClientClosedRequest;
        }

        return Resolve(exception);
    }
}
