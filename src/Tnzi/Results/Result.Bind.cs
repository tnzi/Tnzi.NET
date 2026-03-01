namespace Tnzi.Results;

/// <summary>
/// Result 链式操作扩展 — Bind (可能失败的链式转换)
/// </summary>
public static class ResultBindExtensions
{
    /// <summary>
    /// 链式转换：如果当前结果成功，则执行转换函数（可能返回失败结果），否则直接传递当前失败
    /// </summary>
    public static Result<TNew> Bind<T, TNew>(this Result<T> result, Func<T, Result<TNew>> binder)
    {
        Check.NotNull(result);
        Check.NotNull(binder);

        if (!result.Succeeded || result.Data == null)
            return Result<TNew>.Failure(result.Message ?? "Previous operation failed.", result.Code ?? 400, result.ErrorCode, result.ErrorDetails);

        return binder(result.Data);
    }

    /// <summary>
    /// 异步链式转换：如果当前结果成功，则执行异步转换函数（可能返回失败结果）
    /// </summary>
    public static async Task<Result<TNew>> BindAsync<T, TNew>(this Result<T> result, Func<T, Task<Result<TNew>>> binder)
    {
        Check.NotNull(result);
        Check.NotNull(binder);

        if (!result.Succeeded || result.Data == null)
            return Result<TNew>.Failure(result.Message ?? "Previous operation failed.", result.Code ?? 400, result.ErrorCode, result.ErrorDetails);

        return await binder(result.Data);
    }

    /// <summary>
    /// 异步链式转换：对 Task&lt;Result&lt;T&gt;&gt; 的流畅调用支持
    /// </summary>
    public static async Task<Result<TNew>> BindAsync<T, TNew>(this Task<Result<T>> resultTask, Func<T, Task<Result<TNew>>> binder)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        Check.NotNull(binder);

        var result = await resultTask;
        return await result.BindAsync(binder);
    }

    /// <summary>
    /// 链式转换：对 Task&lt;Result&lt;T&gt;&gt; 的同步 binder 支持
    /// </summary>
    public static async Task<Result<TNew>> Bind<T, TNew>(this Task<Result<T>> resultTask, Func<T, Result<TNew>> binder)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        Check.NotNull(binder);

        var result = await resultTask;
        return result.Bind(binder);
    }

    /// <summary>
    /// 链式无数据 Bind：如果当前结果成功，则执行操作（可能返回失败结果）
    /// </summary>
    public static Result Bind(this Result result, Func<Result> binder)
    {
        Check.NotNull(result);
        Check.NotNull(binder);

        if (!result.Succeeded)
            return result;

        return binder();
    }

    /// <summary>
    /// 异步链式无数据 Bind
    /// </summary>
    public static async Task<Result> BindAsync(this Result result, Func<Task<Result>> binder)
    {
        Check.NotNull(result);
        Check.NotNull(binder);

        if (!result.Succeeded)
            return result;

        return await binder();
    }
}
