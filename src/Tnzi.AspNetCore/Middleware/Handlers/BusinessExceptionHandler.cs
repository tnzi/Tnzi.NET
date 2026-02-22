
namespace Tnzi.AspNetCore.Middleware.Handlers;

/// <summary>
/// 业务异常处理器
/// </summary>
public class BusinessExceptionHandler : ExceptionHandlerBase
{
    public BusinessExceptionHandler(
        IWebHostEnvironment environment,
        IOptionsMonitor<ExceptionHandlingOptions> options,
        ILogger<BusinessExceptionHandler> logger)
        : base(environment, options, logger)
    {
    }

    public override bool CanHandle(Exception exception)
    {
        return exception is BusinessException;
    }

    public override Task<ExceptionHandlingResult> HandleAsync(HttpContext context, Exception exception)
    {
        var businessException = (BusinessException)exception;
        var errorCode = businessException.Code;
        var httpStatusCode = businessException.HttpStatusCode;

        // 获取本地化消息
        var errorMessage = GetLocalizedMessage(context, errorCode, businessException.Message);

        // 处理验证异常的特殊情况
        object? errors = null;
        if (exception is ValidationException validationException && validationException.ValidationErrors != null)
        {
            errors = validationException.ValidationErrors;
        }

        // 处理限流异常，添加 Retry-After 响应头
        if (exception is RateLimitException rateLimitException && rateLimitException.RetryAfter.HasValue)
        {
            context.Response.Headers["Retry-After"] = rateLimitException.RetryAfter.Value.ToString();
        }

        // 添加上下文数据
        object? contextData = null;
        if (CurrentOptions.IncludeContextData && businessException.ContextData != null && businessException.ContextData.Count > 0)
        {
            contextData = businessException.ContextData;
        }

        // 业务异常通常是预期的，使用 Warning 级别记录
        Logger.LogWarning(exception,
            "Business exception occurred - ErrorCode: {ErrorCode}, Message: {Message}",
            errorCode, errorMessage);

        var result = new ExceptionHandlingResult
        {
            StatusCode = httpStatusCode,
            Message = errorMessage,
            ErrorCode = errorCode,
            ErrorDetails = errors,
            ContextData = contextData
        };

        return Task.FromResult(result);
    }
}