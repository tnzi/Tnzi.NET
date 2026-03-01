
namespace Tnzi.Utilities;

/// <summary>
/// 异步辅助操作类
/// </summary>
public static class AsyncHelper
{
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