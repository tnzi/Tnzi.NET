namespace Tnzi.Results;

/// <summary>
/// Result 模式匹配扩展 — 强制处理成功和失败两种状态
/// </summary>
/// <remarks>
/// <para>
/// 分支<b>只依据</b> <see cref="BaseResult{T}.Succeeded"/>：成功走 onSuccess，失败走 onFailure。
/// 「成功但数据为 null」（<c>Result&lt;T?&gt;.Success(null)</c>）是合法状态，会走 <b>成功</b>分支，
/// 并把 null 作为值传给 onSuccess——不要把空数据当失败。
/// </para>
/// <para>
/// <b>何时用链式（Match/Bind/…）</b>：当你需要在一条流水线里把成功值继续变换、且希望失败自动短路传播时，
/// 链式表达更紧凑。<b>何时用命令式</b>：当分支里有复杂副作用、需要多次 await、或需要提前 return 时，
/// 直接判 <c>result.Succeeded</c> 的命令式写法更清晰。
/// </para>
/// </remarks>
public static class ResultMatchExtensions
{
    /// <summary>
    /// 模式匹配：强制处理成功和失败两种状态，返回统一类型的结果。
    /// 成功（含数据为 null）走 onSuccess，失败走 onFailure。
    /// </summary>
    public static TOut Match<T, TOut>(this Result<T> result, Func<T, TOut> onSuccess, Func<Result, TOut> onFailure)
    {
        Check.NotNull(result);
        Check.NotNull(onSuccess);
        Check.NotNull(onFailure);

        // 分支只依据 Succeeded；成功但数据为 null 时把 null 传给 onSuccess（空数据是合法成功值，不算失败）
        return result.Succeeded
            ? onSuccess(result.Data!)
            : onFailure(result);
    }

    /// <summary>
    /// 异步模式匹配。成功（含数据为 null）走 onSuccess，失败走 onFailure。
    /// </summary>
    public static async Task<TOut> MatchAsync<T, TOut>(this Result<T> result, Func<T, Task<TOut>> onSuccess, Func<Result, Task<TOut>> onFailure)
    {
        Check.NotNull(result);
        Check.NotNull(onSuccess);
        Check.NotNull(onFailure);

        // 分支只依据 Succeeded；成功但数据为 null 时把 null 传给 onSuccess
        return result.Succeeded
            ? await onSuccess(result.Data!)
            : await onFailure(result);
    }

    /// <summary>
    /// Task&lt;Result&lt;T&gt;&gt; 的异步模式匹配
    /// </summary>
    public static async Task<TOut> MatchAsync<T, TOut>(this Task<Result<T>> resultTask, Func<T, Task<TOut>> onSuccess, Func<Result, Task<TOut>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(resultTask);

        var result = await resultTask;
        return await result.MatchAsync(onSuccess, onFailure);
    }

    /// <summary>
    /// 无返回值模式匹配（副作用操作）。成功（含数据为 null）走 onSuccess，失败走 onFailure。
    /// </summary>
    public static void Switch<T>(this Result<T> result, Action<T> onSuccess, Action<Result> onFailure)
    {
        Check.NotNull(result);
        Check.NotNull(onSuccess);
        Check.NotNull(onFailure);

        // 分支只依据 Succeeded；成功但数据为 null 时把 null 传给 onSuccess
        if (result.Succeeded)
            onSuccess(result.Data!);
        else
            onFailure(result);
    }

    /// <summary>
    /// 无数据 Result 的模式匹配
    /// </summary>
    public static TOut Match<TOut>(this Result result, Func<TOut> onSuccess, Func<Result, TOut> onFailure)
    {
        Check.NotNull(result);
        Check.NotNull(onSuccess);
        Check.NotNull(onFailure);

        return result.Succeeded ? onSuccess() : onFailure(result);
    }
}
