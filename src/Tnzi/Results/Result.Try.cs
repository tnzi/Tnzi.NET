namespace Tnzi.Results;

/// <summary>
/// Result.Try - 将可能抛异常的操作安全转换为 Result
/// </summary>
public static class ResultTryExtensions
{
    /// <summary>
    /// 执行操作并捕获异常，返回 Result&lt;T&gt;
    /// </summary>
    public static Result<T> Try<T>(Func<T> action)
    {
        Check.NotNull(action);

        try
        {
            return Result<T>.Success(action());
        }
        catch (Exception ex)
        {
            return ResultExtensions.FromException<T>(ex);
        }
    }

    /// <summary>
    /// 执行异步操作并捕获异常，返回 Result&lt;T&gt;
    /// </summary>
    public static async Task<Result<T>> TryAsync<T>(Func<Task<T>> action)
    {
        Check.NotNull(action);

        try
        {
            return Result<T>.Success(await action());
        }
        catch (Exception ex)
        {
            return ResultExtensions.FromException<T>(ex);
        }
    }

    /// <summary>
    /// 执行无返回值操作并捕获异常，返回 Result
    /// </summary>
    public static Result Try(Action action)
    {
        Check.NotNull(action);

        try
        {
            action();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return ResultExtensions.FromException(ex);
        }
    }

    /// <summary>
    /// 执行无返回值异步操作并捕获异常，返回 Result
    /// </summary>
    public static async Task<Result> TryAsync(Func<Task> action)
    {
        Check.NotNull(action);

        try
        {
            await action();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return ResultExtensions.FromException(ex);
        }
    }
}
