
namespace Tnzi.AspNetCore.Mvc.Filters;

/// <summary>
/// 工作单元 Action 过滤器，用于可选标记模式
/// 当用户在 Controller 或 Action 方法上标记 [UnitOfWork] 特性时，该方法中的所有操作都会被包含在一个事务中
/// 任何异常都会自动回滚
/// </summary>
internal class UnitOfWorkActionFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UnitOfWorkActionFilter>? _logger;

    public UnitOfWorkActionFilter(IServiceProvider serviceProvider)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = serviceProvider.GetLogger<UnitOfWorkActionFilter>();
    }

    /// <summary>
    /// 异步执行 Action，管理事务的启用、提交和回滚
    /// </summary>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // 获取 UnitOfWork 服务（优先使用 UnitOfWorkManager）
        var unitOfWorkManager = _serviceProvider.GetService<IUnitOfWorkManager>();
        var unitOfWork = unitOfWorkManager == null ? _serviceProvider.GetService<IUnitOfWork>() : null;
        
        if (unitOfWorkManager == null && unitOfWork == null)
        {
            _logger?.LogWarning("No IUnitOfWork or IUnitOfWorkManager found in service provider. Transaction will not be enabled.");
            await next();
            return;
        }

        // 启用事务（延迟开始，在第一次 SaveChanges 时才真正开始）
        if (unitOfWorkManager != null)
        {
            unitOfWorkManager.EnableTransaction();
            _logger?.LogDebug("Transaction enabled via UnitOfWorkManager for action {Controller}.{Action}",
                context.RouteData.Values["controller"], context.RouteData.Values["action"]);
        }
        else if (unitOfWork != null)
        {
            unitOfWork.EnableTransaction();
            _logger?.LogDebug("Transaction enabled via UnitOfWork for action {Controller}.{Action}",
                context.RouteData.Values["controller"], context.RouteData.Values["action"]);
        }

        try
        {
            // 执行 Action
            var executedContext = await next();

            // 如果发生异常，回滚事务
            if (executedContext.Exception != null && !executedContext.ExceptionHandled)
            {
                await RollbackTransactionAsync(unitOfWorkManager, unitOfWork, executedContext);
                _logger?.LogError(executedContext.Exception, 
                    "Exception occurred in action {Controller}.{Action}, transaction rolled back",
                    context.RouteData.Values["controller"], context.RouteData.Values["action"]);
                return;
            }

            // 检查结果是否成功
            bool isSuccess = IsSuccessResult(executedContext.Result, executedContext.HttpContext.Response.StatusCode);

            if (isSuccess)
            {
                await CommitTransactionAsync(unitOfWorkManager, unitOfWork, executedContext);
            }
            else
            {
                await RollbackTransactionAsync(unitOfWorkManager, unitOfWork, executedContext);
                _logger?.LogWarning("Action {Controller}.{Action} failed, transaction rolled back",
                    context.RouteData.Values["controller"], context.RouteData.Values["action"]);
            }
        }
        catch (Exception ex)
        {
            // 捕获未处理的异常，回滚事务
            await RollbackTransactionAsync(unitOfWorkManager, unitOfWork, null);
            _logger?.LogError(ex, "Unhandled exception in action {Controller}.{Action}, transaction rolled back",
                context.RouteData.Values["controller"], context.RouteData.Values["action"]);
            throw;
        }
    }

    /// <summary>
    /// 判断结果是否成功
    /// 仅 2xx 状态码视为成功（3xx 重定向不应自动提交事务）
    /// </summary>
    private static bool IsSuccessResult(IActionResult? result, int statusCode)
    {
        if (result == null)
        {
            return statusCode >= 200 && statusCode < 300;
        }

        // 检查 ApiResult（使用 IApiResult 接口匹配所有泛型版本）
        if (result is ObjectResult objectResult)
        {
            if (objectResult.Value is IApiResult apiResult)
            {
                return apiResult.Success;
            }

            return (objectResult.StatusCode ?? statusCode) >= 200 &&
                   (objectResult.StatusCode ?? statusCode) < 300;
        }

        // 检查 JsonResult（可能包含 ApiResult）
        if (result is JsonResult jsonResult)
        {
            if (jsonResult.Value is IApiResult apiResult)
            {
                return apiResult.Success;
            }
        }

        // StatusCodeResult（204 No Content 等）
        if (result is StatusCodeResult statusResult)
        {
            return statusResult.StatusCode >= 200 && statusResult.StatusCode < 300;
        }

        return statusCode >= 200 && statusCode < 300;
    }

    /// <summary>
    /// 提交事务
    /// </summary>
    private async Task CommitTransactionAsync(IUnitOfWorkManager? unitOfWorkManager, IUnitOfWork? unitOfWork, ActionExecutedContext? context)
    {
        try
        {
            if (unitOfWorkManager != null)
            {
                await unitOfWorkManager.CommitTransactionAsync();
            }
            else if (unitOfWork != null)
            {
                await unitOfWork.CommitTransactionAsync();
            }
        }
        catch (Exception ex)
        {
            var controllerName = context?.RouteData.Values["controller"]?.ToString() ?? "Unknown";
            var actionName = context?.RouteData.Values["action"]?.ToString() ?? "Unknown";
            _logger?.LogError(ex, "Failed to commit transaction for action {Controller}.{Action}",
                controllerName, actionName);
            throw;
        }
    }

    /// <summary>
    /// 回滚事务
    /// </summary>
    private async Task RollbackTransactionAsync(IUnitOfWorkManager? unitOfWorkManager, IUnitOfWork? unitOfWork, ActionExecutedContext? context)
    {
        try
        {
            if (unitOfWorkManager != null)
            {
                await unitOfWorkManager.RollbackTransactionAsync();
            }
            else if (unitOfWork != null)
            {
                await unitOfWork.RollbackTransactionAsync();
            }
        }
        catch (Exception ex)
        {
            var controllerName = context?.RouteData.Values["controller"]?.ToString() ?? "Unknown";
            var actionName = context?.RouteData.Values["action"]?.ToString() ?? "Unknown";
            _logger?.LogError(ex, "Failed to rollback transaction for action {Controller}.{Action}",
                controllerName, actionName);
            // 回滚失败不应该阻止异常传播，只记录错误
        }
    }
}