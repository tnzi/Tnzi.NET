namespace Tnzi.EventBus;

/// <summary>
/// 事件死信队列接口
/// 用于存储处理失败的事件，支持后续重试或人工处理
/// </summary>
public interface IEventDeadLetterQueue
{
    /// <summary>
    /// 将失败的事件添加到死信队列
    /// </summary>
    /// <typeparam name="TEvent">事件类型</typeparam>
    /// <param name="event">事件对象</param>
    /// <param name="handlerType">处理器类型</param>
    /// <param name="exception">失败异常</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task AddAsync<TEvent>(TEvent @event, Type handlerType, Exception exception, CancellationToken cancellationToken = default)
        where TEvent : class, IEvent;

    /// <summary>
    /// 获取死信队列中的所有事件
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>死信事件列表</returns>
    Task<IReadOnlyList<DeadLetterEvent>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 从死信队列中移除指定的事件
    /// </summary>
    /// <param name="eventId">事件ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task RemoveAsync(Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空死信队列
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 死信事件信息
/// </summary>
public class DeadLetterEvent
{
    /// <summary>
    /// 事件ID
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// 事件类型名称
    /// </summary>
    public string EventTypeName { get; set; } = string.Empty;

    /// <summary>
    /// 事件对象（序列化后的 JSON）
    /// </summary>
    public string EventData { get; set; } = string.Empty;

    /// <summary>
    /// 处理器类型名称
    /// </summary>
    public string HandlerTypeName { get; set; } = string.Empty;

    /// <summary>
    /// 失败异常信息
    /// </summary>
    public string ExceptionMessage { get; set; } = string.Empty;

    /// <summary>
    /// 失败异常堆栈跟踪
    /// </summary>
    public string? ExceptionStackTrace { get; set; }

    /// <summary>
    /// 失败时间
    /// </summary>
    public DateTime FailedAt { get; set; }

    /// <summary>
    /// 重试次数
    /// </summary>
    public int RetryCount { get; set; }
}
