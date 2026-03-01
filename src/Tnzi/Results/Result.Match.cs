namespace Tnzi.Results;

/// <summary>
/// Result 模式匹配扩展 — 强制处理成功和失败两种状态
/// </summary>
public static class ResultMatchExtensions
{
    /// <summary>
    /// 模式匹配：强制处理成功和失败两种状态，返回统一类型的结果
    /// </summary>
    public static TOut Match<T, TOut>(this Result<T> result, Func<T, TOut> onSuccess, Func<Result, TOut> onFailure)
    {
        Check.NotNull(result);
        Check.NotNull(onSuccess);
        Check.NotNull(onFailure);

        return result.Succeeded && result.Data != null
            ? onSuccess(result.Data)
            : onFailure(result);
    }

    /// <summary>
    /// 异步模式匹配
    /// </summary>
    public static async Task<TOut> MatchAsync<T, TOut>(this Result<T> result, Func<T, Task<TOut>> onSuccess, Func<Result, Task<TOut>> onFailure)
    {
        Check.NotNull(result);
        Check.NotNull(onSuccess);
        Check.NotNull(onFailure);

        return result.Succeeded && result.Data != null
            ? await onSuccess(result.Data)
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
    /// 无返回值模式匹配（副作用操作）
    /// </summary>
    public static void Switch<T>(this Result<T> result, Action<T> onSuccess, Action<Result> onFailure)
    {
        Check.NotNull(result);
        Check.NotNull(onSuccess);
        Check.NotNull(onFailure);

        if (result.Succeeded && result.Data != null)
            onSuccess(result.Data);
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
