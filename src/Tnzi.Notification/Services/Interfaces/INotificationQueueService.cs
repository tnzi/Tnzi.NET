namespace Tnzi.Notification.Services;

/// <summary>
/// 通知队列服务接口
/// </summary>
/// <remarks>
/// ★ 工作项是 <see cref="NotificationWorkItem"/> 而不是裸委托：它自带所属租户，执行时经
/// <see cref="NotificationWorkItem.RunAsync"/> 切回去。自定义实现（Hangfire / 消息队列）
/// 只需在自己的执行侧调用 <c>RunAsync</c>，并把 <see cref="NotificationWorkItem.TenantId"/>
/// 随任务一起持久化（跨进程时委托本身序列化不了，那就存 messageId + tenantId，
/// 执行侧再用 <see cref="NotificationWorkItem.SendMessage"/> 重建）。
/// </remarks>
public interface INotificationQueueService
{
    /// <summary>
    /// 将任务加入队列
    /// </summary>
    Task EnqueueAsync(NotificationWorkItem workItem);

    /// <summary>
    /// 将任务延迟后加入队列（用于重试退避）
    /// </summary>
    Task EnqueueWithDelayAsync(NotificationWorkItem workItem, TimeSpan delay)
    {
        // 默认实现：忽略延迟直接入队
        return EnqueueAsync(workItem);
    }
}
