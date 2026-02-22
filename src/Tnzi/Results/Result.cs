namespace Tnzi.Results;

/// <summary>
/// 服务层操作结果（无数据版本）
/// 用于服务层返回操作结果，不包含具体数据
/// </summary>
/// <remarks>
/// 用途：
/// - 服务层方法返回操作结果（如删除、更新等不需要返回数据的操作）
/// - 包含 HTTP 状态码、错误代码等 API 相关信息
/// - 通过 ToApiResult() 转换为 ApiResult 用于 Controller 返回
///
/// 使用场景：
/// - 删除操作：Result.Success("删除成功")
/// - 更新操作：Result.Failure("更新失败", 400, errorCode: "VALIDATION_ERROR")
/// </remarks>
[StableApi(Since = "0.1.0")]
public class Result : BaseResult
{
    /// <summary>
    /// HTTP 状态码（可选，用于 API 层）
    /// </summary>
    public int? Code { get; protected set; }

    /// <summary>
    /// 业务错误代码（字符串格式，如 "VALIDATION_ERROR", "NOT_FOUND" 等）
    /// </summary>
    public string? ErrorCode { get; protected set; }

    /// <summary>
    /// 错误详情（统一错误信息，可以是字符串列表、对象等）
    /// 注意：此属性已统一 Errors 和 ErrorData，推荐使用此属性
    /// </summary>
    public object? ErrorDetails { get; protected set; }

    /// <summary>
    /// 初始化结果
    /// </summary>
    protected Result(bool succeeded, string? message = null, int? code = null,
        string? errorCode = null, object? errorDetails = null)
        : base(succeeded, message)
    {
        Code = code;
        ErrorCode = errorCode;
        ErrorDetails = errorDetails;
    }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    /// <param name="message">成功消息</param>
    /// <returns>成功结果</returns>
    public static Result Success(string? message = null)
    {
        return new Result(true, message, 200);
    }

    /// <summary>
    /// 创建成功结果（带数据，向后兼容）
    /// </summary>
    public static Result<T> Success<T>(T data, string? message = null, int? code = null)
    {
        return Result<T>.Success(data, message, code);
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="code">HTTP 状态码</param>
    /// <param name="errorCode">业务错误代码</param>
    /// <param name="errorDetails">错误详情（可以是字符串列表、对象等）</param>
    /// <returns>失败结果</returns>
    public static Result Failure(string message, int code = 400,
        string? errorCode = null, object? errorDetails = null)
    {
        return new Result(false, message, code, errorCode, errorDetails);
    }

    /// <summary>
    /// 创建失败结果（泛型便捷方法）
    /// </summary>
    public static Result<T> Failure<T>(string message, int code = 400,
        string? errorCode = null, object? errorDetails = null)
    {
        return Result<T>.Failure(message, code, errorCode, errorDetails);
    }

}

/// <summary>
/// 服务层操作结果（带数据版本）
/// 用于服务层返回操作结果，包含具体数据
/// </summary>
/// <typeparam name="T">数据类型</typeparam>
/// <remarks>
/// 用途：
/// - 服务层方法返回操作结果和具体数据（如查询、创建等需要返回数据的操作）
/// - 包含 HTTP 状态码、错误代码等 API 相关信息
/// - 通过 ToApiResult() 转换为 ApiResult 用于 Controller 返回
///
/// 使用场景：
/// - 查询操作：Result&lt;UserDto&gt;.Success(userDto, "查询成功")
/// - 创建操作：Result&lt;Guid&gt;.Success(userId, "创建成功")
/// - 失败操作：Result&lt;UserDto&gt;.Failure("用户不存在", 404, errorCode: "USER_NOT_FOUND")
///
/// 继承关系：
/// - 继承 Result，提供 HTTP 状态码、错误代码等 API 相关属性
/// - 隐藏基类的 Data 属性（object?），提供强类型的 Data 属性（T?）
/// </remarks>
[StableApi(Since = "0.1.0")]
public class Result<T> : Result
{
    /// <summary>
    /// 结果数据（强类型版本，隐藏基类的 object? Data）
    /// </summary>
    public new T? Data { get; protected set; }

    /// <summary>
    /// 初始化结果
    /// </summary>
    protected Result(bool succeeded, T? data = default, string? message = null,
        int? code = null, string? errorCode = null, object? errorDetails = null)
        : base(succeeded, message, code, errorCode, errorDetails)
    {
        Data = data;
        // 同步设置基类的 Data 属性（用于兼容性）
        base.Data = data;
    }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    /// <param name="data">数据</param>
    /// <param name="message">成功消息</param>
    /// <param name="code">HTTP 状态码（可选，默认为 200）</param>
    /// <returns>成功结果</returns>
    public static Result<T> Success(T data, string? message = null, int? code = null)
    {
        return new Result<T>(true, data, message, code ?? 200);
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="code">HTTP 状态码</param>
    /// <param name="errorCode">业务错误代码</param>
    /// <param name="errorDetails">错误详情（可以是字符串列表、对象等）</param>
    /// <returns>失败结果</returns>
    public static new Result<T> Failure(string message, int code = 400,
        string? errorCode = null, object? errorDetails = null)
    {
        return new Result<T>(false, default, message, code, errorCode, errorDetails);
    }

    /// <summary>
    /// 将成功结果映射为新类型
    /// </summary>
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        Check.NotNull(mapper);
        if (!Succeeded)
            return Result<TNew>.Failure(Message ?? "Unknown error", Code ?? 400, ErrorCode, ErrorDetails);
        return Result<TNew>.Success(mapper(Data!), Message, Code);
    }

}
