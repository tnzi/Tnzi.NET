
namespace Tnzi.AspNetCore.Middleware.Handlers;

/// <summary>
/// 默认异常处理器
/// 处理所有其他类型的异常
/// </summary>
public class DefaultExceptionHandler : ExceptionHandlerBase
{
    public DefaultExceptionHandler(
        IWebHostEnvironment environment,
        IOptionsMonitor<ExceptionHandlingOptions> options,
        ILogger<DefaultExceptionHandler> logger)
        : base(environment, options, logger)
    {
    }

    /// <summary>
    /// 最低优先级：本处理器 <see cref="CanHandle"/> 恒为 true，必须排在链尾。
    /// 若与其它处理器同为默认的 0，链的相对次序就取决于 List.Sort 的稳定性
    /// （文档明确不保证），一旦本处理器被排到业务处理器之前，404/403 等
    /// 业务状态码会被统统压成 500。
    /// </summary>
    public override int Priority => int.MinValue;

    public override bool CanHandle(Exception exception)
    {
        // 默认处理器可以处理所有异常（作为最后的备选）
        return true;
    }

    public override Task<ExceptionHandlingResult> HandleAsync(HttpContext context, Exception exception)
    {
        int httpStatusCode;
        string errorMessage;
        string? errorCode = null;
        string? errorDetail = null;

        // 处理框架异常（TnziException）
        if (exception is TnziException tnziException)
        {
            httpStatusCode = 500;
            errorCode = tnziException.Code;

            // 获取本地化消息
            errorMessage = GetLocalizedMessage(context, errorCode, tnziException.Message) ?? string.Empty;

            // 开发环境可以暴露详细错误
            if (IsDevelopment && CurrentOptions.ShowDetailsInDevelopment)
            {
                errorDetail = tnziException.ToString();
            }

            // 框架异常记录为 Error 级别
            Logger.LogError(exception,
                "Framework exception occurred - Code: {ErrorCode}, Message: {Message}",
                errorCode, errorMessage);
        }
        // 处理系统异常（非框架异常）
        else
        {
            httpStatusCode = 500;

            if (IsDevelopment && CurrentOptions.ShowDetailsInDevelopment)
            {
                // 开发环境：暴露详细异常信息
                errorMessage = exception.Message ?? string.Empty;
                errorDetail = exception.ToString();
            }
            else
            {
                // 生产环境：返回通用错误消息，不暴露具体异常
                errorCode = INTERNAL_SERVER_ERROR;
                errorMessage = GetLocalizedMessage(context, INTERNAL_SERVER_ERROR, "An error occurred while processing your request.") ?? string.Empty;
            }

            // 记录详细异常信息到日志（无论什么环境都记录）
            Logger.LogError(exception,
                "System exception occurred - Type: {ExceptionType}, Message: {ExceptionMessage}, StackTrace: {StackTrace}",
                exception.GetType().Name, exception.Message, exception.StackTrace);
        }

        var result = new ExceptionHandlingResult
        {
            StatusCode = httpStatusCode,
            Message = errorMessage,
            ErrorCode = errorCode,
            ErrorDetail = errorDetail
        };

        return Task.FromResult(result);
    }
}