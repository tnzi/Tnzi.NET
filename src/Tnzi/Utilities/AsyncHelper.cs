
namespace Tnzi.Utilities;

/// <summary>
/// 异步辅助操作类
/// </summary>
public static class AsyncHelper
{
    /// <summary>
    /// 同步执行异步方法（阻塞当前线程直到完成）
    /// </summary>
    /// <typeparam name="TResult">返回类型</typeparam>
    /// <param name="asyncMethod">异步方法</param>
    /// <returns>异步方法的返回值</returns>
    /// <remarks>
    /// 警告：此方法会阻塞当前线程，应谨慎使用。
    /// 使用 Task.Run 将任务调度到线程池以避免同步上下文死锁。
    /// 建议：在可能的情况下，优先使用异步方法而不是此同步包装器。
    /// </remarks>
    [Obsolete("Use async/await instead. This method may cause deadlocks.")]
    public static TResult RunSync<TResult>(Func<Task<TResult>> asyncMethod)
    {
        Check.NotNull(asyncMethod);

        // 使用 Task.Run 避免同步上下文死锁，内部使用 ConfigureAwait(false) 进一步防护
        return Task.Run(async () => await asyncMethod().ConfigureAwait(false)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 同步执行异步方法（阻塞当前线程直到完成）
    /// </summary>
    /// <param name="asyncMethod">异步方法</param>
    /// <remarks>
    /// 警告：此方法会阻塞当前线程，应谨慎使用。
    /// 使用 Task.Run 将任务调度到线程池以避免同步上下文死锁。
    /// 建议：在可能的情况下，优先使用异步方法而不是此同步包装器。
    /// </remarks>
    [Obsolete("Use async/await instead. This method may cause deadlocks.")]
    public static void RunSync(Func<Task> asyncMethod)
    {
        Check.NotNull(asyncMethod);

        // 使用 Task.Run 避免同步上下文死锁，内部使用 ConfigureAwait(false) 进一步防护
        Task.Run(async () => await asyncMethod().ConfigureAwait(false)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 执行Task并处理Finally
    /// </summary>
    public static async Task AwaitTaskWithFinally(Task returnValueTask, Action<Exception>? finalAction)
    {
        Exception? exception = null;
        try
        {
            await returnValueTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            if (exception != null)
            {
                finalAction?.Invoke(exception);
            }
        }
    }

    /// <summary>
    /// 执行Task并获取结果，同时处理Finally
    /// </summary>
    public static async Task<T> AwaitTaskWithFinallyAndGetResult<T>(Task<T> actualReturnValue, Action<Exception>? finalAction)
    {
        Exception? exception = null;
        try
        {
            return await actualReturnValue.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            if (exception != null)
            {
                finalAction?.Invoke(exception);
            }
        }
    }
}