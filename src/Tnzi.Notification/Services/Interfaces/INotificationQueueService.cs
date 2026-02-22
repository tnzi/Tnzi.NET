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
}
