
namespace Tnzi.AspNetCore.Middleware.Handlers;

/// <summary>
/// 验证异常处理器
/// 验证异常是业务异常的子类，但为了更好的组织代码，单独创建处理器
/// </summary>
public class ValidationExceptionHandler : BusinessExceptionHandler
{
    /// <summary>
    /// 验证异常优先级高于普通业务异常
    /// </summary>
    public override int Priority => 10;

    public ValidationExceptionHandler(
        IWebHostEnvironment environment,
        IOptionsMonitor<ExceptionHandlingOptions> options,
        ILogger<ValidationExceptionHandler> logger)
        : base(environment, options, logger)
    {
    }

    public override bool CanHandle(Exception exception)
    {
        return exception is ValidationException;
    }

    public override async Task<ExceptionHandlingResult> HandleAsync(HttpContext context, Exception exception)
    {
        // 验证异常是业务异常，使用基类处理，但确保验证错误被包含
        var result = await base.HandleAsync(context, exception);

        if (exception is ValidationException validationException && validationException.ValidationErrors != null)
        {
            result.ErrorDetails = validationException.ValidationErrors;
        }

        return result;
    }
}