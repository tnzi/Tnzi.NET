namespace Tnzi.Results;

/// <summary>
/// Result 错误恢复/降级扩展 - 当结果失败时提供备选值或替换错误
/// </summary>
public static class ResultElseExtensions
{
    /// <summary>
    /// 错误恢复：如果结果失败，使用备选值恢复为成功状态
    /// </summary>
    public static Result<T> Else<T>(this Result<T> result, T fallbackValue)
    {
        Check.NotNull(result);

        return result.Succeeded ? result : Result<T>.Success(fallbackValue);
    }

    /// <summary>
    /// 错误恢复：如果结果失败，使用工厂函数创建备选值（可访问失败信息）
    /// </summary>
    public static Result<T> Else<T>(this Result<T> result, Func<Result, T> fallbackFactory)
    {
        Check.NotNull(result);
        Check.NotNull(fallbackFactory);

        return result.Succeeded ? result : Result<T>.Success(fallbackFactory(result));
    }

    /// <summary>
    /// 异步错误恢复：如果结果失败，使用异步工厂函数创建备选值
    /// </summary>
    public static async Task<Result<T>> ElseAsync<T>(this Result<T> result, Func<Result, Task<T>> fallbackFactory)
    {
        Check.NotNull(result);
        Check.NotNull(fallbackFactory);

        return result.Succeeded ? result : Result<T>.Success(await fallbackFactory(result));
    }

    /// <summary>
    /// Task&lt;Result&lt;T&gt;&gt; 的错误恢复
    /// </summary>
    public static async Task<Result<T>> Else<T>(this Task<Result<T>> resultTask, T fallbackValue)
    {
        ArgumentNullException.ThrowIfNull(resultTask);

        var result = await resultTask;
        return result.Else(fallbackValue);
    }

    /// <summary>
    /// Task&lt;Result&lt;T&gt;&gt; 的异步错误恢复
    /// </summary>
    public static async Task<Result<T>> ElseAsync<T>(this Task<Result<T>> resultTask, Func<Result, Task<T>> fallbackFactory)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        Check.NotNull(fallbackFactory);

        var result = await resultTask;
        return await result.ElseAsync(fallbackFactory);
    }
}
