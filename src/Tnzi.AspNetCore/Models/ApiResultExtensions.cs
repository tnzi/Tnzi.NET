namespace Tnzi.AspNetCore.Models;

/// <summary>
/// ApiResult 扩展方法
/// </summary>
public static class ApiResultExtensions
{
    /// <summary>
    /// 创建 400 BadRequest 响应
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="errors">详细错误信息</param>
    /// <returns>API结果</returns>
    public static ApiResult BadRequest(string message = "Bad Request", object? errors = null)
    {
        return ApiResult.Error(message, 400, null, errors);
    }

    /// <summary>
    /// 创建 400 BadRequest 响应（带数据）
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="message">错误消息</param>
    /// <param name="errors">详细错误信息</param>
    /// <returns>API结果</returns>
    public static ApiResult<T> BadRequest<T>(string message = "Bad Request", object? errors = null)
    {
        return ApiResult<T>.Error(message, 400, null, errors);
    }

    /// <summary>
    /// 创建 401 Unauthorized 响应
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <returns>API结果</returns>
    public static ApiResult Unauthorized(string message = "Unauthorized")
    {
        return ApiResult.Error(message, 401);
    }

    /// <summary>
    /// 创建 401 Unauthorized 响应（带数据）
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="message">错误消息</param>
    /// <returns>API结果</returns>
    public static ApiResult<T> Unauthorized<T>(string message = "Unauthorized")
    {
        return ApiResult<T>.Error(message, 401);
    }

    /// <summary>
    /// 创建 403 Forbidden 响应
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <returns>API结果</returns>
    public static ApiResult Forbidden(string message = "Forbidden")
    {
        return ApiResult.Error(message, 403);
    }

    /// <summary>
    /// 创建 403 Forbidden 响应（带数据）
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="message">错误消息</param>
    /// <returns>API结果</returns>
    public static ApiResult<T> Forbidden<T>(string message = "Forbidden")
    {
        return ApiResult<T>.Error(message, 403);
    }

    /// <summary>
    /// 创建 404 NotFound 响应
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <returns>API结果</returns>
    public static ApiResult NotFound(string message = "Resource not found")
    {
        return ApiResult.Error(message, 404);
    }

    /// <summary>
    /// 创建 404 NotFound 响应（带数据）
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="message">错误消息</param>
    /// <returns>API结果</returns>
    public static ApiResult<T> NotFound<T>(string message = "Resource not found")
    {
        return ApiResult<T>.Error(message, 404);
    }

    /// <summary>
    /// 创建 500 InternalServerError 响应
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <returns>API结果</returns>
    public static ApiResult InternalError(string message = "Internal server error")
    {
        return ApiResult.Error(message, 500);
    }

    /// <summary>
    /// 创建 500 InternalServerError 响应（带数据）
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="message">错误消息</param>
    /// <returns>API结果</returns>
    public static ApiResult<T> InternalError<T>(string message = "Internal server error")
    {
        return ApiResult<T>.Error(message, 500);
    }
}

