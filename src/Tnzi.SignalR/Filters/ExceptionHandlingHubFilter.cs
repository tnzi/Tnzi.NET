namespace Tnzi.SignalR.Filters;

/// <summary>
/// Hub 异常处理过滤器
/// 将内部异常转换为安全的 HubException
/// </summary>
public class ExceptionHandlingHubFilter : IHubFilter
{
    private readonly ILogger<ExceptionHandlingHubFilter> _logger;
    private readonly bool _enableDetailedErrors;

    /// <summary>
    /// 初始化一个<see cref="ExceptionHandlingHubFilter"/>类型的新实例
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="options">SignalR配置选项</param>
    public ExceptionHandlingHubFilter(ILogger<ExceptionHandlingHubFilter> logger, IOptions<SignalROptions> options)
    {
        _logger = Check.NotNull(logger);
        _enableDetailedErrors = Check.NotNull(options).Value?.Hub?.EnableDetailedErrors ?? false;
    }

    /// <summary>
    /// 调用 Hub 方法时的异常处理
    /// </summary>
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var hubName = invocationContext.Hub.GetType().Name;
        var methodName = invocationContext.HubMethodName;

        try
        {
            return await next(invocationContext);
        }
        catch (HubException)
        {
            // HubException 直接传递给客户端
            throw;
        }
        catch (ForbiddenException ex)
        {
            // 权限拒绝异常
            _logger.LogWarning(ex,
                "Forbidden access in hub method {HubName}.{MethodName}",
                hubName, methodName);
            throw new HubException(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            // 未授权异常
            _logger.LogWarning(ex,
                "Unauthorized access in hub method {HubName}.{MethodName}",
                hubName, methodName);
            throw new HubException("Access denied.");
        }
        catch (ArgumentException ex)
        {
            // 参数异常
            _logger.LogWarning(ex,
                "Invalid argument in hub method {HubName}.{MethodName}",
                hubName, methodName);
            throw new HubException(_enableDetailedErrors ? ex.Message : "Invalid request parameters.");
        }
        catch (InvalidOperationException ex)
        {
            // 无效操作异常
            _logger.LogWarning(ex,
                "Invalid operation in hub method {HubName}.{MethodName}",
                hubName, methodName);
            throw new HubException(_enableDetailedErrors ? ex.Message : "Invalid operation.");
        }
        catch (Exception ex)
        {
            // 其他未处理异常
            _logger.LogError(ex,
                "Unhandled exception in hub method {HubName}.{MethodName}",
                hubName, methodName);

            if (_enableDetailedErrors)
            {
                throw new HubException($"Internal error: {ex.Message}");
            }

            throw new HubException("An error occurred while processing your request.");
        }
    }
}
