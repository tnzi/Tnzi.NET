namespace Tnzi.Notification.Services;

/// <summary>
/// 通知队列服务接口
/// </summary>
public interface INotificationQueueService
{
    /// <summary>
    /// 将任务加入队列
    /// </summary>
    Task EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem);

    /// <summary>
    /// 将任务延迟后加入队列（用于重试退避）
    /// </summary>
    Task EnqueueWithDelayAsync(Func<IServiceProvider, CancellationToken, Task> workItem, TimeSpan delay)
    {
        // 默认实现：忽略延迟直接入队
        return EnqueueAsync(workItem);
    }
}
