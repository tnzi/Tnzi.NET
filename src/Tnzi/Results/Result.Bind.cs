namespace Tnzi.Results;

/// <summary>
/// Result 链式操作扩展 — Bind (可能失败的链式转换)
/// </summary>
/// <remarks>
/// <para>
/// 分支<b>只依据</b> <see cref="BaseResult{T}.Succeeded"/>：当前结果成功则执行 binder（binder 自身仍可返回失败），
/// 当前结果失败则原样短路传播失败信息（Message/Code/ErrorCode/ErrorDetails 不丢失）。
/// 「成功但数据为 null」是合法状态，会执行 binder 并把 null 作为值传入——空数据<b>不</b>被当作失败。
/// </para>
/// <para>
/// <b>何时用链式</b>：多步可能失败的转换串联、希望第一处失败自动短路后续步骤时用 Bind。
/// <b>何时用命令式</b>：步骤间需要保留多个中间变量、或有非线性控制流时，直接判 Succeeded 更直观。
/// </para>
/// </remarks>
public static class ResultBindExtensions
{
    /// <summary>
    /// 链式转换：如果当前结果成功（含数据为 null），则执行转换函数（可能返回失败结果），否则直接传递当前失败
    /// </summary>
    public static Result<TNew> Bind<T, TNew>(this Result<T> result, Func<T, Result<TNew>> binder)
    {
        Check.NotNull(result);
        Check.NotNull(binder);

        // 分支只依据 Succeeded：失败才短路，成功但数据为 null 仍执行 binder（空数据是合法成功值）
        if (!result.Succeeded)
            return Result<TNew>.Failure(result.Message ?? "Previous operation failed.", result.Code ?? 400, result.ErrorCode, result.ErrorDetails);

        return binder(result.Data!);
    }

    /// <summary>
    /// 异步链式转换：如果当前结果成功（含数据为 null），则执行异步转换函数（可能返回失败结果）
    /// </summary>
    public static async Task<Result<TNew>> BindAsync<T, TNew>(this Result<T> result, Func<T, Task<Result<TNew>>> binder)
    {
        Check.NotNull(result);
        Check.NotNull(binder);

        // 分支只依据 Succeeded：失败才短路，成功但数据为 null 仍执行 binder
        if (!result.Succeeded)
            return Result<TNew>.Failure(result.Message ?? "Previous operation failed.", result.Code ?? 400, result.ErrorCode, result.ErrorDetails);

        return await binder(result.Data!);
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
