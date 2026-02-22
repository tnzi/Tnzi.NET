
namespace Tnzi.AspNetCore.Models;

/// <summary>
/// ApiResult 标记接口
/// </summary>
public interface IApiResult
{
    /// <summary>
    /// HTTP 状态码
    /// </summary>
    int Code { get; }

    /// <summary>
    /// 是否成功
    /// </summary>
    bool Success { get; }
}

/// <summary>
/// API 层操作结果（带数据版本）
/// 用于 Controller 返回 HTTP 响应，是最终的 API 响应格式
/// </summary>
/// <typeparam name="T">数据类型</typeparam>
/// <remarks>
/// 用途：
/// - Controller 方法的返回类型，直接序列化为 JSON 响应
/// - 包含完整的 HTTP 响应信息（状态码、消息、数据、错误信息）
/// - 继承 Result&lt;T&gt;，自动获得服务层的所有功能
/// </remarks>
[StableApi(Since = "0.1.0")]
public class ApiResult<T> : Result<T>, IApiResult
{
    /// <summary>
    /// HTTP 状态码（非空，覆盖父类的可空 Code）
    /// </summary>
    public new int Code { get; private set; }

    /// <summary>
    /// 是否成功（2xx 状态码视为成功）
    /// 提供更符合 API 习惯的命名
    /// </summary>
    public new bool Success => Code >= 200 && Code < 300;


    /// <summary>
    /// 初始化 API 结果
    /// </summary>
    protected ApiResult(bool succeeded, T? data = default, string? message = null,
        int code = 200, string? errorCode = null, object? errorDetails = null)
        : base(succeeded, data, message, code, errorCode, errorDetails)
    {
        Code = code;  // 确保 Code 非空
    }

    /// <summary>
    /// 创建成功响应
    /// </summary>
    /// <param name="data">数据</param>
    /// <param name="message">成功消息</param>
    /// <returns>成功响应</returns>
    public static ApiResult<T> Ok(T data, string message = "Success")
    {
        return new ApiResult<T>(true, data, message, 200);
    }

    /// <summary>
    /// 创建错误响应
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="code">HTTP 状态码</param>
    /// <param name="errorCode">业务错误代码</param>
    /// <param name="errors">错误详情（向后兼容参数名）</param>
    /// <returns>错误响应</returns>
    public static ApiResult<T> Error(string message, int code = 500,
        string? errorCode = null, object? errors = null)
    {
        return new ApiResult<T>(false, default, message, code, errorCode, errors);
    }

    /// <summary>
    /// 从 Result 创建 ApiResult
    /// 用于将服务层结果转换为 API 响应
    /// </summary>
    /// <param name="result">服务层结果</param>
    /// <returns>API 响应</returns>
    public static ApiResult<T> FromResult(Result<T> result)
    {
        if (result.Succeeded)
        {
            return Ok(result.Data!, result.Message ?? "Success");
        }
        else
        {
            return Error(
                result.Message ?? "Operation failed",
                result.Code ?? 400,
                result.ErrorCode,
                result.ErrorDetails);
        }
    }
}

/// <summary>
/// API 层操作结果（无数据版本）
/// 用于 Controller 返回 HTTP 响应，不包含具体数据
/// </summary>
[StableApi(Since = "0.1.0")]
public class ApiResult : ApiResult<object>
{
    /// <summary>
    /// 初始化 API 结果（无数据版本）
    /// </summary>
    private ApiResult(bool succeeded, object? data = default, string? message = null,
        int code = 200, string? errorCode = null, object? errorDetails = null)
        : base(succeeded, data, message, code, errorCode, errorDetails)
    {
    }

    /// <summary>
    /// 创建成功响应
    /// </summary>
    /// <param name="message">成功消息</param>
    /// <returns>成功响应</returns>
    public static ApiResult Ok(string message = "Success")
    {
        return new ApiResult(true, null, message, 200);
    }

    /// <summary>
    /// 创建错误响应
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="code">HTTP 状态码</param>
    /// <param name="errorCode">业务错误代码</param>
    /// <param name="errors">错误详情（向后兼容参数名）</param>
    /// <returns>错误响应</returns>
    public static new ApiResult Error(string message, int code = 500,
        string? errorCode = null, object? errors = null)
    {
        return new ApiResult(false, null, message, code, errorCode, errors);
    }

    /// <summary>
    /// 从 Result 创建 ApiResult
    /// 用于将服务层结果转换为 API 响应（无数据版本）
    /// </summary>
    public static ApiResult FromResult(Result result)
    {
        if (result.Succeeded)
        {
            return Ok(result.Message ?? "Success");
        }
        else
        {
            return Error(
                result.Message ?? "Operation failed",
                result.Code ?? 400,
                result.ErrorCode,
                result.ErrorDetails);
        }
    }
}