
namespace Tnzi.AspNetCore.Middleware.Handlers;

/// <summary>
/// 基础设施异常处理器
/// </summary>
public class InfrastructureExceptionHandler : ExceptionHandlerBase
{
    public InfrastructureExceptionHandler(
        IWebHostEnvironment environment,
        IOptionsMonitor<ExceptionHandlingOptions> options,
        ILogger<InfrastructureExceptionHandler> logger)
        : base(environment, options, logger)
    {
    }

    public override bool CanHandle(Exception exception)
    {
        return exception is InfrastructureException;
    }

    public override Task<ExceptionHandlingResult> HandleAsync(HttpContext context, Exception exception)
    {
        var infrastructureException = (InfrastructureException)exception;
        var errorCode = infrastructureException.Code;
        var httpStatusCode = 503; // Service Unavailable
        var isRetryable = infrastructureException.IsRetryable;

        // 获取本地化消息
        var errorMessage = GetLocalizedMessage(context, errorCode, infrastructureException.Message);

        // 如果是配置异常，在生产环境不暴露详细错误
        if (exception is ConfigurationException && !IsDevelopment)
        {
            errorMessage = GetLocalizedMessage(context, "CONFIGURATION_ERROR", "Configuration error occurred.");
        }

        // 添加上下文数据
        object? contextData = null;
        if (CurrentOptions.IncludeContextData && infrastructureException.ContextData != null && infrastructureException.ContextData.Count > 0)
        {
            contextData = infrastructureException.ContextData;
        }

        // 基础设施异常记录为 Error 级别
        Logger.LogError(exception,
            "Infrastructure exception occurred - Component: {ComponentName}, Code: {ErrorCode}, Message: {Message}, Retryable: {IsRetryable}",
            infrastructureException.ComponentName, errorCode, errorMessage, isRetryable);

        var result = new ExceptionHandlingResult
        {
            StatusCode = httpStatusCode,
            Message = errorMessage,
            ErrorCode = errorCode,
            ContextData = contextData,
            IsRetryable = isRetryable
        };

        return Task.FromResult(result);
    }
}