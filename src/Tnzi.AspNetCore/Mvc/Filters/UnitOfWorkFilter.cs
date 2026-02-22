
namespace Tnzi.AspNetCore.Mvc.Filters;

/// <summary>
/// 工作单元过滤器，自动管理数据库事务
/// 支持两种模式：
/// 1. 全局模式：所有 Action 自动应用事务（通过配置 EnableGlobalUnitOfWork = true）
/// 2. 可选标记模式：只有标记了 [UnitOfWork] 的 Action 才应用事务（默认模式）
/// </summary>
public class UnitOfWorkFilter : IAsyncActionFilter
{
    private readonly IUnitOfWorkManager? _unitOfWorkManager;
    private readonly IUnitOfWork? _unitOfWork;
    private readonly ILogger<UnitOfWorkFilter> _logger;
    private readonly bool _enableGlobalUnitOfWork;

    public UnitOfWorkFilter(
        IServiceProvider serviceProvider,
        ILogger<UnitOfWorkFilter> logger,
        IOptions<AspNetCoreOptions>? aspNetCoreOptions = null)
    {
        _logger = Check.NotNull(logger);

        // 优先尝试获取 UnitOfWorkManager
        _unitOfWorkManager = serviceProvider.GetService<IUnitOfWorkManager>();
        if (_unitOfWorkManager == null)
        {
            // 如果没有 UnitOfWorkManager，尝试获取单个 IUnitOfWork
            _unitOfWork = serviceProvider.GetService<IUnitOfWork>();
        }
        
        _enableGlobalUnitOfWork = aspNetCoreOptions?.Value?.EnableGlobalUnitOfWork ?? false;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // 检查是否标记了禁用工作单元
        var isDisabled = context.ActionDescriptor.EndpointMetadata
            .OfType<UnitOfWorkAttribute>()
            .Any(attr => attr.IsDisabled);

        if (isDisabled)
        {
            await next();
            return;
        }

        // 检查是否有 [UnitOfWork] 标记（用于可选标记模式）
        var hasUnitOfWorkAttribute = context.ActionDescriptor.EndpointMetadata
            .OfType<UnitOfWorkAttribute>()
            .Any(attr => !attr.IsDisabled);

        // 只有在全局模式或标记了 [UnitOfWork] 时才启用事务
        if (!_enableGlobalUnitOfWork && !hasUnitOfWorkAttribute)
        {
            await next();
            return;
        }

        // 启用事务（延迟开始，在第一次 SaveChanges 时才真正开始）
        if (_unitOfWorkManager != null)
        {
            _unitOfWorkManager.EnableTransaction();
        }
        else if (_unitOfWork != null)
        {
            _unitOfWork.EnableTransaction();
        }
        else
        {
            await next();
            return;
        }

        try
        {
            // 执行 Action
            var executedContext = await next();

            // 判断是否需要提交
            bool shouldCommit = IsSuccessResult(executedContext.Result, context.HttpContext.Response.StatusCode, executedContext.Exception);

            // 提交或回滚事务
            if (shouldCommit)
            {
                if (_unitOfWorkManager != null)
                {
                    await _unitOfWorkManager.CommitTransactionAsync();
                }
                else if (_unitOfWork != null)
                {
                    await _unitOfWork.CommitTransactionAsync();
                }
            }
            else
            {
                if (_unitOfWorkManager != null)
                {
                    await _unitOfWorkManager.RollbackTransactionAsync();
                }
                else if (_unitOfWork != null)
                {
                    await _unitOfWork.RollbackTransactionAsync();
                }
            }
        }
        catch (Exception ex)
        {
            // 发生异常，回滚事务
            if (_unitOfWorkManager != null)
            {
                await _unitOfWorkManager.RollbackTransactionAsync();
            }
            else if (_unitOfWork != null)
            {
                await _unitOfWork.RollbackTransactionAsync();
            }
            _logger.LogError(ex, "Transaction rolled back due to exception. Action: {Controller}.{Action}",
                context.RouteData.Values["controller"], context.RouteData.Values["action"]);
            throw;
        }
    }

    /// <summary>
    /// 判断结果是否成功
    /// 仅 2xx 状态码视为成功（3xx 重定向不应自动提交事务）
    /// </summary>
    private static bool IsSuccessResult(IActionResult? result, int statusCode, Exception? exception)
    {
        if (exception != null)
        {
            return false;
        }

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
}