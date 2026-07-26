
namespace Tnzi.AspNetCore.Extensions;

/// <summary>
/// Result 扩展方法
/// 提供 Result 类之间的转换功能
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// 将 Result&lt;T&gt; 转换为 ApiResult&lt;T&gt;
    /// 用于 Controller 返回 HTTP 响应
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="result">服务层结果</param>
    /// <returns>API 响应</returns>
    /// <remarks>
    /// 用途：
    /// - 在 Controller 中将服务层返回的 Result 转换为 ApiResult
    /// - 自动处理成功/失败情况，设置正确的 HTTP 状态码
    ///
    /// 使用场景：
    /// - Controller 方法：return (await service.DoSomethingAsync()).ToApiResult()
    /// </remarks>
    public static ApiResult<T> ToApiResult<T>(this Result<T> result)
    {
        Check.NotNull(result);
        return ApiResult<T>.FromResult(result);
    }

    /// <summary>
    /// 将 Result 转换为 ApiResult（无数据版本）
    /// 用于 Controller 返回 HTTP 响应（无数据版本）
    /// </summary>
    /// <param name="result">服务层结果</param>
    /// <returns>API 响应</returns>
    /// <remarks>
    /// 用途：
    /// - 在 Controller 中将服务层返回的 Result（无数据）转换为 ApiResult
    /// - 自动处理成功/失败情况，设置正确的 HTTP 状态码
    ///
    /// 使用场景：
    /// - Controller 方法：return (await service.DeleteAsync(id)).ToApiResult()
    /// </remarks>
    public static ApiResult ToApiResult(this Result result)
    {
        Check.NotNull(result);
        if (result.Succeeded)
        {
            return ApiResult.Ok(result.Message ?? "Success");
        }
        else
        {
            return ApiResult.Error(
                result.Message ?? "Operation failed",
                result.Code ?? 400,
                result.ErrorCode,
                result.ErrorDetails);
        }
    }

    /// <summary>
    /// 将 Result 转换为 ApiResult&lt;T&gt;
    /// 用于 Controller 返回 HTTP 响应（带指定类型）
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="result">服务层结果</param>
    /// <returns>API 响应</returns>
    /// <remarks>
    /// 用途：
    /// - 在 Controller 中将服务层返回的 Result（无数据）转换为指定类型的 ApiResult
    /// - 自动处理成功/失败情况，设置正确的 HTTP 状态码
    ///
    /// 使用场景：
    /// - Controller 方法：return (await service.DeleteAsync(id)).ToApiResult&lt;string&gt;()
    /// </remarks>
    public static ApiResult<T> ToApiResult<T>(this Result result)
    {
        Check.NotNull(result);
        if (result.Succeeded)
        {
            return ApiResult<T>.Ok(default(T)!, result.Message ?? "Success");
        }
        else
        {
            return ApiResult<T>.Error(
                result.Message ?? "Operation failed",
                result.Code ?? 400,
                result.ErrorCode,
                result.ErrorDetails);
        }
    }
}