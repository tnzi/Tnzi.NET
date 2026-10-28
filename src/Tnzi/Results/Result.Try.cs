namespace Tnzi.Results;

/// <summary>
/// 将可能抛异常的操作转换为 Result（<c>ResultTryExtensions.Try(...)</c> / <c>ResultTryExtensions.TryAsync(...)</c>）。
/// </summary>
/// <remarks>
/// 异常按 <see cref="ResultExtensions.FromException(Exception)"/> 映射：<see cref="BusinessException"/> 保留消息与状态码，
/// 其它框架异常保留消息、错误码与上下文数据，非框架异常只给固定文案（原始消息可能带表名、约束名、主机地址，
/// 而失败结果经 <c>ToApiResult</c> 原样到达客户端）。需要排查细节的调用方在委托里自己记日志，或自己 catch。
/// <para>取消不转换：<see cref="OperationCanceledException"/> 原样抛出，调用方的取消照常向上传播。
/// 唯一的例外是 HttpClient 超时（内含 <see cref="TimeoutException"/>），那是被调用方的失败，照常转换为失败结果。</para>
/// </remarks>
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
        catch (Exception ex) when (!IsPropagatedCancellation(ex))
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
        catch (Exception ex) when (!IsPropagatedCancellation(ex))
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
        catch (Exception ex) when (!IsPropagatedCancellation(ex))
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
        catch (Exception ex) when (!IsPropagatedCancellation(ex))
        {
            return ResultExtensions.FromException(ex);
        }
    }

    /// <summary>
    /// 调用方的取消必须向上传播而不是变成一次失败：后台任务据失败结果把记录标成「失败」，
    /// 请求取消被记成一次 500。HttpClient 超时抛的是内含 <see cref="TimeoutException"/> 的
    /// <see cref="TaskCanceledException"/>，它表示被调用方没有及时响应，按失败处理。
    /// </summary>
    private static bool IsPropagatedCancellation(Exception ex)
        => ex is OperationCanceledException && ex.InnerException is not TimeoutException;
}
