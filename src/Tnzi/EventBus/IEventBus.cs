namespace Tnzi.EventBus;

/// <summary>
/// 事件接口（标记接口）
/// </summary>
[StableApi(Since = "0.1.0")]
public interface IEvent
{
    /// <summary>
    /// 获取事件ID
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// 获取事件发生时间
    /// </summary>
    DateTime EventTime { get; }
}

/// <summary>
/// 领域事件接口
/// 用于进程内的领域模型间通信，保证强一致性
/// </summary>
[StableApi(Since = "0.1.0")]
public interface IDomainEvent : IEvent
{
}

/// <summary>
/// 集成事件接口
/// 用于跨服务/跨进程的通信，支持最终一致性
/// </summary>
[StableApi(Since = "0.1.0")]
public interface IIntegrationEvent : IEvent
{
    /// <summary>
    /// 获取事件来源服务名称
    /// </summary>
    string SourceService { get; }

    /// <summary>
    /// 获取事件版本号（用于向后兼容）
    /// </summary>
    int Version => 1;
}

/// <summary>
/// 事件处理器接口
/// </summary>
/// <typeparam name="TEvent">事件类型</typeparam>
[StableApi(Since = "0.1.0")]
public interface IEventHandler<in TEvent> where TEvent : IEvent
{
    /// <summary>
    /// 处理事件
    /// </summary>
    /// <param name="event">事件对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}

/// <summary>
/// 集成事件总线接口（跨服务事件发布）
/// 用于微服务间的异步通信
/// </summary>
[StableApi(Since = "0.1.0")]
public interface IIntegrationEventBus
{
    /// <summary>
    /// 发布集成事件到消息队列
    /// </summary>
    /// <typeparam name="TEvent">集成事件类型</typeparam>
    /// <param name="event">事件对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent;

    /// <summary>
    /// 订阅集成事件
    /// </summary>
    /// <typeparam name="TEvent">集成事件类型</typeparam>
    /// <typeparam name="THandler">处理器类型</typeparam>
    void Subscribe<TEvent, THandler>()
        where TEvent : class, IIntegrationEvent
        where THandler : IEventHandler<TEvent>;
}

/// <summary>
/// 条件事件处理器接口
/// 实现此接口的处理器可以通过 CanHandle 方法决定是否处理事件
/// </summary>
/// <typeparam name="TEvent">事件类型</typeparam>
[StableApi(Since = "0.1.0")]
public interface IConditionalEventHandler<in TEvent> : IEventHandler<TEvent>
    where TEvent : class, IEvent
{
    /// <summary>
    /// 判断是否可以处理该事件
    /// </summary>
    /// <param name="event">事件对象</param>
    /// <returns>是否可以处理</returns>
    bool CanHandle(TEvent @event);
}

/// <summary>
/// 事件订阅者接口
/// 提供运行时动态订阅和取消订阅事件处理器的能力
/// </summary>
[StableApi(Since = "0.1.0")]
public interface IEventSubscriber
{
    /// <summary>
    /// 运行时订阅事件处理器类型
    /// </summary>
    /// <typeparam name="TEvent">事件类型</typeparam>
    /// <typeparam name="THandler">处理器类型</typeparam>
    void Subscribe<TEvent, THandler>()
        where TEvent : class, IEvent
        where THandler : class, IEventHandler<TEvent>;

    /// <summary>
    /// 取消订阅指定事件类型的特定处理器类型
    /// </summary>
    /// <typeparam name="TEvent">事件类型</typeparam>
    /// <typeparam name="THandler">处理器类型</typeparam>
    void Unsubscribe<TEvent, THandler>()
        where TEvent : class, IEvent
        where THandler : class, IEventHandler<TEvent>;

    /// <summary>
    /// 取消订阅指定事件类型的所有处理器（仅运行时注册的）
    /// </summary>
    /// <typeparam name="TEvent">事件类型</typeparam>
    void UnsubscribeAll<TEvent>() where TEvent : class, IEvent;
}

/// <summary>
/// 事件总线接口（进程内事件发布）
/// </summary>
[StableApi(Since = "0.1.0")]
public interface IEventBus : IEventSubscriber
{
    /// <summary>
    /// Gets whether this event bus is a local (in-process) implementation.
    /// </summary>
    bool IsLocal => true;

    /// <summary>
    /// 发布事件
    /// </summary>
    /// <typeparam name="TEvent">事件类型</typeparam>
    /// <param name="event">事件对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class, IEvent;

    /// <summary>
    /// 延迟发布事件
    /// </summary>
    /// <typeparam name="TEvent">事件类型</typeparam>
    /// <param name="event">事件对象</param>
    /// <param name="delay">延迟时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task PublishDelayedAsync<TEvent>(TEvent @event, TimeSpan delay, CancellationToken cancellationToken = default) where TEvent : class, IEvent;

    /// <summary>
    /// 检查指定事件类型是否有处理器
    /// </summary>
    /// <typeparam name="TEvent">事件类型</typeparam>
    /// <returns>是否存在处理器</returns>
    bool HasHandlers<TEvent>() where TEvent : class, IEvent;

    /// <summary>
    /// 获取指定事件类型的处理器数量
    /// </summary>
    /// <typeparam name="TEvent">事件类型</typeparam>
    /// <returns>处理器数量</returns>
    int GetHandlerCount<TEvent>() where TEvent : class, IEvent;
}

