
namespace Tnzi.Results;

/// <summary>
/// Result 扩展方法
/// 提供链式验证、从异常创建、批量组合等功能
/// </summary>
public static class ResultExtensions
{
    #region 链式验证

    /// <summary>
    /// 链式验证：如果当前结果成功（含数据为 null），则执行验证函数，否则返回当前失败结果
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="result">当前结果</param>
    /// <param name="validator">验证函数，返回验证结果；成功但数据为 null 时以 null 调用</param>
    /// <returns>验证后的结果</returns>
    public static Result<T> Validate<T>(this Result<T> result, Func<T, Result> validator)
    {
        Check.NotNull(result);
        Check.NotNull(validator);

        // 分支只依据 Succeeded：空数据是合法成功值，交给 validator 决定（不再把 null 当失败）
        if (!result.Succeeded)
            return result;

        var validationResult = validator(result.Data!);
        if (!validationResult.Succeeded)
        {
            return Result<T>.Failure(
                validationResult.Message ?? "Validation failed.",
                validationResult.Code ?? 400,
                validationResult.ErrorCode ?? ErrorCodes.VALIDATION_ERROR,
                validationResult.ErrorDetails);
        }

        return result;
    }

    /// <summary>
    /// 链式验证：如果当前结果成功，则执行验证函数，否则返回当前失败结果
    /// </summary>
    /// <param name="result">当前结果</param>
    /// <param name="validator">验证函数，返回验证结果</param>
    /// <returns>验证后的结果</returns>
    public static Result Validate(this Result result, Func<Result> validator)
    {
        Check.NotNull(result);
        Check.NotNull(validator);

        if (!result.Succeeded)
            return result;

        var validationResult = validator();
        if (!validationResult.Succeeded)
        {
            return Result.Failure(
                validationResult.Message ?? "Validation failed.",
                validationResult.Code ?? 400,
                validationResult.ErrorCode ?? ErrorCodes.VALIDATION_ERROR,
                validationResult.ErrorDetails);
        }

        return result;
    }

    /// <summary>
    /// 链式验证：如果当前结果成功（含数据为 null），则执行验证函数，否则返回当前结果
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="result">当前结果</param>
    /// <param name="validator">验证函数，如果验证失败返回错误消息，否则返回 null；成功但数据为 null 时以 null 调用</param>
    /// <returns>验证后的结果</returns>
    public static Result<T> Validate<T>(this Result<T> result, Func<T, string?> validator)
    {
        Check.NotNull(result);
        Check.NotNull(validator);

        // 分支只依据 Succeeded：空数据是合法成功值，交给 validator 决定（不再静默跳过 null）
        if (!result.Succeeded)
            return result;

        var errorMessage = validator(result.Data!);
        if (!string.IsNullOrEmpty(errorMessage))
        {
            return Result<T>.Failure(errorMessage, 400, ErrorCodes.VALIDATION_ERROR);
        }

        return result;
    }

    #endregion

    #region 从异常创建结果

    /// <summary>
    /// 从异常创建失败结果
    /// </summary>
    /// <param name="exception">异常</param>
    /// <returns>失败结果</returns>
    public static Result FromException(Exception exception)
    {
        Check.NotNull(exception);

        return exception switch
        {
            BusinessException businessEx => Result.Failure(
                businessEx.Message,
                businessEx.HttpStatusCode,
                businessEx.Code,
                businessEx.ContextData),

            TnziException tnziEx => Result.Failure(
                tnziEx.Message,
                500,
                tnziEx.Code,
                tnziEx.ContextData),

            _ => Result.Failure(
                exception.Message,
                500,
                ErrorCodes.INTERNAL_SERVER_ERROR,
                new { ExceptionType = exception.GetType().Name })
        };
    }

    /// <summary>
    /// 从异常创建失败结果（带数据类型）
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="exception">异常</param>
    /// <returns>失败结果</returns>
    public static Result<T> FromException<T>(Exception exception)
    {
        Check.NotNull(exception);

        return exception switch
        {
            BusinessException businessEx => Result<T>.Failure(
                businessEx.Message,
                businessEx.HttpStatusCode,
                businessEx.Code,
                businessEx.ContextData),

            TnziException tnziEx => Result<T>.Failure(
                tnziEx.Message,
                500,
                tnziEx.Code,
                tnziEx.ContextData),

            _ => Result<T>.Failure(
                exception.Message,
                500,
                ErrorCodes.INTERNAL_SERVER_ERROR,
                new { ExceptionType = exception.GetType().Name })
        };
    }

    #endregion

    #region 批量结果组合

    /// <summary>
    /// 组合多个结果，如果所有结果都成功则返回成功，否则返回第一个失败的结果
    /// </summary>
    /// <param name="results">结果集合</param>
    /// <returns>组合后的结果</returns>
    public static Result Combine(params Result[] results)
    {
        if (results == null || results.Length == 0)
            return Result.Success();

        foreach (var result in results)
        {
            if (result == null)
                continue;

            if (!result.Succeeded)
                return result;
        }

        return Result.Success();
    }

    /// <summary>
    /// 组合多个结果，如果所有结果都成功则返回成功，否则返回第一个失败的结果
    /// </summary>
    /// <param name="results">结果集合</param>
    /// <returns>组合后的结果</returns>
    public static Result Combine(IEnumerable<Result> results)
    {
        if (results == null)
            return Result.Success();

        foreach (var result in results)
        {
            if (result == null)
                continue;

            if (!result.Succeeded)
                return result;
        }

        return Result.Success();
    }

    /// <summary>
    /// 组合多个结果，返回所有失败的结果集合
    /// </summary>
    /// <param name="results">结果集合</param>
    /// <returns>所有失败的结果集合</returns>
    public static IReadOnlyList<Result> GetFailures(params Result[] results)
    {
        if (results == null || results.Length == 0)
            return Array.Empty<Result>();

        return results
            .Where(r => r != null && !r.Succeeded)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// 组合多个结果，返回所有失败的结果集合
    /// </summary>
    /// <param name="results">结果集合</param>
    /// <returns>所有失败的结果集合</returns>
    public static IReadOnlyList<Result> GetFailures(IEnumerable<Result> results)
    {
        if (results == null)
            return Array.Empty<Result>();

        return results
            .Where(r => r != null && !r.Succeeded)
            .ToList()
            .AsReadOnly();
    }

    #endregion

    #region 条件操作

    /// <summary>
    /// 如果结果成功（含数据为 null），则执行操作
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="result">结果</param>
    /// <param name="action">要执行的操作，成功但数据为 null 时以 null 调用</param>
    /// <returns>原结果</returns>
    public static Result<T> OnSuccess<T>(this Result<T> result, Action<T> action)
    {
        Check.NotNull(result);
        Check.NotNull(action);

        // 分支只依据 Succeeded；成功但数据为 null 时把 null 传给 action（空数据是合法成功值）
        if (result.Succeeded)
        {
            action(result.Data!);
        }

        return result;
    }

    /// <summary>
    /// 如果结果成功，则执行操作
    /// </summary>
    /// <param name="result">结果</param>
    /// <param name="action">要执行的操作</param>
    /// <returns>原结果</returns>
    public static Result OnSuccess(this Result result, Action action)
    {
        Check.NotNull(result);
        Check.NotNull(action);

        if (result.Succeeded)
        {
            action();
        }

        return result;
    }

    /// <summary>
    /// 如果结果失败，则执行操作
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="result">结果</param>
    /// <param name="action">要执行的操作</param>
    /// <returns>原结果</returns>
    public static Result<T> OnFailure<T>(this Result<T> result, Action<string?, string?> action)
    {
        Check.NotNull(result);
        Check.NotNull(action);

        if (!result.Succeeded)
        {
            action(result.Message, result.ErrorCode);
        }

        return result;
    }

    /// <summary>
    /// 如果结果失败，则执行操作
    /// </summary>
    /// <param name="result">结果</param>
    /// <param name="action">要执行的操作</param>
    /// <returns>原结果</returns>
    public static Result OnFailure(this Result result, Action<string?, string?> action)
    {
        Check.NotNull(result);
        Check.NotNull(action);

        if (!result.Succeeded)
        {
            action(result.Message, result.ErrorCode);
        }

        return result;
    }

    #endregion
}