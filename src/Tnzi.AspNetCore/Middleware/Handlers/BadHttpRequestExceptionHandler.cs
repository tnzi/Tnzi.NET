namespace Tnzi.AspNetCore.Middleware.Handlers;

/// <summary>
/// 请求本身读不了的异常处理器：ASP.NET Core 的 <see cref="BadHttpRequestException"/>
/// （体没读完客户端就走了、请求体超过上限、读体超时、表单格式错误）按它自带的 4xx 答复。
/// </summary>
/// <remarks>
/// <para>
/// 这些都是客户端造成的失败。没有这个处理器时它落到 <see cref="DefaultExceptionHandler"/>：答 500、记 Error，
/// 用户关掉正在上传的页面就成了一条「服务器错误」。状态码与观测类中间件同一张表（<see cref="ExceptionHttpStatusCode"/>）。
/// </para>
/// <para>
/// 消息用异常自己的：它描述的是客户端发来的请求（「Unexpected end of request content.」「Request body too large.」），
/// 不涉及服务端内部状态。日志不记 Error：客户端已经断开时记 Debug，其余记 Information。
/// </para>
/// </remarks>
public class BadHttpRequestExceptionHandler : ExceptionHandlerBase
{
    public BadHttpRequestExceptionHandler(
        IWebHostEnvironment environment,
        IOptionsMonitor<ExceptionHandlingOptions> options,
        ILogger<BadHttpRequestExceptionHandler> logger)
        : base(environment, options, logger)
    {
    }

    public override bool CanHandle(Exception exception)
    {
        return exception is BadHttpRequestException;
    }

    public override Task<ExceptionHandlingResult> HandleAsync(HttpContext context, Exception exception)
    {
        var badRequest = (BadHttpRequestException)exception;
        var httpStatusCode = GetHttpStatusCode(badRequest);

        var logLevel = context.RequestAborted.IsCancellationRequested ? LogLevel.Debug : LogLevel.Information;
        Logger.Log(logLevel, exception,
            "Request {Method} {Path} could not be read - StatusCode: {StatusCode}, Message: {Message}",
            context.Request.Method, context.GetRedactedPath(), httpStatusCode, badRequest.Message);

        var result = new ExceptionHandlingResult
        {
            StatusCode = httpStatusCode,
            Message = GetLocalizedMessage(context, BAD_REQUEST, badRequest.Message),
            ErrorCode = BAD_REQUEST
        };

        return Task.FromResult(result);
    }
}
