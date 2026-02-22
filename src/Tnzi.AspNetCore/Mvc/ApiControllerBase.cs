
namespace Tnzi.AspNetCore.Mvc;

/// <summary>
/// API控制器基类，提供常用服务和便捷响应方法
/// </summary>
[ApiController]
[StableApi(Since = "0.1.0")]
public abstract class ApiControllerBase : ControllerBase
{
    protected readonly IServiceProvider? ServiceProvider;
    private ICurrentUser? _currentUser;
    private ILogger? _logger;

    /// <summary>
    /// 最小构造函数（只包含必需的服务）
    /// </summary>
    protected ApiControllerBase()
    {
    }

    /// <summary>
    /// 完整构造函数（包含可选服务）
    /// </summary>
    /// <param name="serviceProvider">服务提供者（可选）</param>
    protected ApiControllerBase(IServiceProvider? serviceProvider = null)
    {
        ServiceProvider = serviceProvider;
    }

    /// <summary>
    /// 获取服务提供者（延迟获取，优先使用注入的，否则从 HttpContext 获取）
    /// </summary>
    protected IServiceProvider Services =>
        ServiceProvider ?? HttpContext.RequestServices;

    /// <summary>
    /// 日志记录器（延迟加载）
    /// </summary>
    protected ILogger Logger =>
        _logger ??= Services.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());

    /// <summary>
    /// 当前用户服务（延迟加载）
    /// </summary>
    protected ICurrentUser? CurrentUser =>
        _currentUser ??= Services.GetService<ICurrentUser>();

    /// <summary>
    /// 获取当前用户服务（必需），如果未注册则抛出异常
    /// </summary>
    /// <returns>当前用户服务</returns>
    /// <exception cref="InvalidOperationException">服务未注册时抛出</exception>
    protected ICurrentUser GetRequiredCurrentUser()
    {
        var currentUser = Services.GetService<ICurrentUser>();
        if (currentUser == null)
            throw new InvalidOperationException($"Service {nameof(ICurrentUser)} is not registered. Please ensure the Identity module is loaded.");

        return currentUser;
    }

    /// <summary>
    /// 获取必需的服务，如果未注册则抛出异常
    /// </summary>
    /// <typeparam name="T">服务类型</typeparam>
    /// <returns>服务实例</returns>
    /// <exception cref="InvalidOperationException">服务未注册时抛出</exception>
    protected T GetRequiredService<T>() where T : notnull
    {
        var service = Services.GetService<T>();
        if (service == null)
            throw new InvalidOperationException($"Service {typeof(T).Name} is not registered. Please ensure the required module is loaded.");

        return service;
    }

    /// <summary>
    /// 检查服务是否可用
    /// </summary>
    /// <typeparam name="T">服务类型</typeparam>
    /// <returns>服务是否可用</returns>
    protected bool IsServiceAvailable<T>() where T : class
    {
        return Services.GetService<T>() != null;
    }

    /// <summary>
    /// 模型验证错误响应
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="message">错误消息（可选，默认使用第一个验证错误）</param>
    /// <returns>API结果</returns>
    protected ApiResult<T> ValidationError<T>(string? message = null)
    {
        var errors = ModelState.GetValidationErrors();
        return ApiResult<T>.Error(
            message ?? ModelState.FirstError() ?? "Validation failed",
            400,
            null,
            errors);
    }

    /// <summary>
    /// 模型验证错误响应（无数据）
    /// </summary>
    /// <param name="message">错误消息（可选，默认使用第一个验证错误）</param>
    /// <returns>API结果</returns>
    protected ApiResult ValidationError(string? message = null)
    {
        var errors = ModelState.GetValidationErrors();
        return ApiResult.Error(
            message ?? ModelState.FirstError() ?? "Validation failed",
            400,
            null,
            errors);
    }

    /// <summary>
    /// 成功响应（带数据）
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="data">响应数据</param>
    /// <param name="message">响应消息</param>
    /// <returns>API结果</returns>
    protected ApiResult<T> Ok<T>(T data, string message = "Success")
    {
        return ApiResult<T>.Ok(data, message);
    }

    /// <summary>
    /// 成功响应（无数据）
    /// </summary>
    /// <param name="message">响应消息</param>
    /// <returns>API结果</returns>
    protected ApiResult Ok(string message = "Success")
    {
        return ApiResult.Ok(message);
    }

    /// <summary>
    /// 错误响应
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="message">错误消息</param>
    /// <param name="code">HTTP状态码</param>
    /// <param name="errorCode">业务错误代码</param>
    /// <param name="errors">详细错误信息</param>
    /// <returns>API结果</returns>
    protected ApiResult<T> Error<T>(string message, int code = 500, string? errorCode = null, object? errors = null)
    {
        return ApiResult<T>.Error(message, code, errorCode, errors);
    }

    /// <summary>
    /// 错误响应（无数据）
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="code">HTTP状态码</param>
    /// <param name="errorCode">业务错误代码</param>
    /// <param name="errors">详细错误信息</param>
    /// <returns>API结果</returns>
    protected ApiResult Error(string message, int code = 500, string? errorCode = null, object? errors = null)
    {
        return ApiResult.Error(message, code, errorCode, errors);
    }

    /// <summary>
    /// 400 BadRequest 响应
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="message">错误消息</param>
    /// <param name="errors">详细错误信息</param>
    /// <returns>API结果</returns>
    protected ApiResult<T> BadRequest<T>(string message = "Bad Request", object? errors = null)
    {
        return ApiResult<T>.Error(message, 400, null, errors);
    }

    /// <summary>
    /// 400 BadRequest 响应（无数据）
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="errors">详细错误信息</param>
    /// <returns>API结果</returns>
    protected ApiResult BadRequest(string message = "Bad Request", object? errors = null)
    {
        return ApiResult.Error(message, 400, null, errors);
    }

    /// <summary>
    /// 401 Unauthorized 响应
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="message">错误消息</param>
    /// <returns>API结果</returns>
    protected ApiResult<T> Unauthorized<T>(string message = "Unauthorized")
    {
        return ApiResult<T>.Error(message, 401);
    }

    /// <summary>
    /// 401 Unauthorized 响应（无数据）
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <returns>API结果</returns>
    protected ApiResult Unauthorized(string message = "Unauthorized")
    {
        return ApiResult.Error(message, 401);
    }

    /// <summary>
    /// 403 Forbidden 响应
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="message">错误消息</param>
    /// <returns>API结果</returns>
    protected ApiResult<T> Forbidden<T>(string message = "Forbidden")
    {
        return ApiResult<T>.Error(message, 403);
    }

    /// <summary>
    /// 403 Forbidden 响应（无数据）
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <returns>API结果</returns>
    protected ApiResult Forbidden(string message = "Forbidden")
    {
        return ApiResult.Error(message, 403);
    }

    /// <summary>
    /// 404 NotFound 响应
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="message">错误消息</param>
    /// <returns>API结果</returns>
    protected ApiResult<T> NotFound<T>(string message = "Resource not found")
    {
        return ApiResult<T>.Error(message, 404);
    }

    /// <summary>
    /// 404 NotFound 响应（无数据）
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <returns>API结果</returns>
    protected ApiResult NotFound(string message = "Resource not found")
    {
        return ApiResult.Error(message, 404);
    }
}