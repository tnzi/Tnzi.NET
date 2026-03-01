namespace Tnzi.Results;

/// <summary>
/// Result 条件创建和链式条件失败扩展
/// </summary>
public static class ResultConditionExtensions
{
    #region 条件创建工厂

    /// <summary>
    /// 条件创建：条件为 true 时返回成功，否则返回失败
    /// </summary>
    public static Result OkIf(bool condition, string failureMessage, int code = 400, string? errorCode = null)
    {
        return condition ? Result.Success() : Result.Failure(failureMessage, code, errorCode);
    }

    /// <summary>
    /// 条件创建：条件为 true 时返回成功（带数据），否则返回失败
    /// </summary>
    public static Result<T> OkIf<T>(bool condition, T data, string failureMessage, int code = 400, string? errorCode = null)
    {
        return condition ? Result<T>.Success(data) : Result<T>.Failure(failureMessage, code, errorCode);
    }

    /// <summary>
    /// 条件创建：条件为 true 时返回失败，否则返回成功
    /// </summary>
    public static Result FailIf(bool condition, string failureMessage, int code = 400, string? errorCode = null)
    {
        return condition ? Result.Failure(failureMessage, code, errorCode) : Result.Success();
    }

    /// <summary>
    /// 条件创建：条件为 true 时返回失败，否则返回成功（带数据）
    /// </summary>
    public static Result<T> FailIf<T>(bool condition, T data, string failureMessage, int code = 400, string? errorCode = null)
    {
        return condition ? Result<T>.Failure(failureMessage, code, errorCode) : Result<T>.Success(data);
    }

    #endregion

    #region 链式条件失败

    /// <summary>
    /// 链式条件失败：如果结果成功且数据满足条件，则转为失败状态
    /// </summary>
    public static Result<T> FailIf<T>(this Result<T> result, Func<T, bool> predicate, string failureMessage, int code = 400, string? errorCode = null)
    {
        Check.NotNull(result);
        Check.NotNull(predicate);

        if (!result.Succeeded || result.Data == null)
            return result;

        return predicate(result.Data)
            ? Result<T>.Failure(failureMessage, code, errorCode)
            : result;
    }

    /// <summary>
    /// 链式条件失败：支持惰性错误消息构建（可访问当前值）
    /// </summary>
    public static Result<T> FailIf<T>(this Result<T> result, Func<T, bool> predicate, Func<T, string> failureMessageFactory, int code = 400, string? errorCode = null)
    {
        Check.NotNull(result);
        Check.NotNull(predicate);
        Check.NotNull(failureMessageFactory);

        if (!result.Succeeded || result.Data == null)
            return result;

        return predicate(result.Data)
            ? Result<T>.Failure(failureMessageFactory(result.Data), code, errorCode)
            : result;
    }

    #endregion
}
