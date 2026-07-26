
namespace Tnzi.AspNetCore.Mvc.Filters;

/// <summary>
/// API 结果自动包装过滤器
/// 当 AutoWrapApiResult 为 true 时，自动将非 ApiResult 的返回值包装为 ApiResult
/// 实现 IOrderedFilter 确保在其他过滤器之后执行
/// </summary>
public class ApiResultWrapperFilter : IResultFilter, IOrderedFilter
{
    private readonly AspNetCoreOptions _options;

    /// <summary>
    /// 过滤器执行顺序（尽可能最后执行，确保其他过滤器修改结果后再包装）
    /// </summary>
    public int Order => int.MaxValue - 1;

    /// <summary>
    /// 初始化一个<see cref="ApiResultWrapperFilter"/>类型的新实例
    /// </summary>
    public ApiResultWrapperFilter(IOptions<AspNetCoreOptions> options)
    {
        _options = Check.NotNull(options).Value;
    }

    /// <summary>
    /// 结果执行前
    /// </summary>
    public void OnResultExecuting(ResultExecutingContext context)
    {
        // 如果未启用自动包装，直接返回
        if (!_options.AutoWrapApiResult)
            return;

        // 只处理 ObjectResult
        if (context.Result is not ObjectResult objectResult)
            return;

        // 如果已经是 ApiResult，不需要包装
        if (objectResult.Value is IApiResult)
            return;

        // 从 ObjectResult 或 HttpContext.Response 获取实际状态码
        var statusCode = objectResult.StatusCode
            ?? context.HttpContext.Response.StatusCode;
        ApiResult<object> apiResult;

        if (statusCode >= 200 && statusCode < 300)
        {
            // 成功状态码，包装为 Ok
            apiResult = ApiResult<object>.Ok(objectResult.Value ?? new object());
        }
        else
        {
            // 错误状态码，安全提取错误消息
            var errorMessage = ExtractErrorMessage(objectResult.Value);
            apiResult = ApiResult<object>.Error(errorMessage, statusCode);
        }

        // 创建新的 ObjectResult，保留原始状态码
        context.Result = new ObjectResult(apiResult)
        {
            StatusCode = statusCode
        };
    }

    /// <summary>
    /// 结果执行后
    /// </summary>
    public void OnResultExecuted(ResultExecutedContext context)
    {
        // 无需处理
    }

    /// <summary>
    /// 安全提取错误消息
    /// 避免 ToString() 返回类型名而非实际错误信息
    /// </summary>
    private static string ExtractErrorMessage(object? value)
    {
        if (value == null)
            return "An error occurred";

        if (value is string str)
            return str;

        if (value is Exception ex)
            return ex.Message;

        // 尝试从对象的 Message 或 message 属性中提取
        var type = value.GetType();
        var messageProp = type.GetProperty("Message") ?? type.GetProperty("message");
        if (messageProp?.GetValue(value) is string msg)
            return msg;

        // 如果都不行，检查 ToString() 是否被重写（避免返回类型名）
        var toString = value.ToString();
        if (toString != null && toString != type.FullName && toString != type.Name)
            return toString;

        return "An error occurred";
    }
}