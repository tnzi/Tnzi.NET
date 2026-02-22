

namespace Tnzi.RabbitMQ;

/// <summary>
/// RabbitMQ事件总线实现
/// 同时实现IEventBus和IIntegrationEventBus接口
/// </summary>
public class RabbitMQEventBus : IEventBus, IIntegrationEventBus, IDisposable
{
    private readonly IConnection _connection;
    private readonly ILogger<RabbitMQEventBus> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly RabbitMQOptions _options;
    private readonly string _exchangeName;
    private readonly ConcurrentDictionary<Type, string> _queueNames = new();

    // 延迟初始化的 channel，替代构造函数中的 AsyncHelper.RunSync
    private IChannel? _channel;
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private bool _channelInitialized;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsLocal => false;

    /// <summary>
    /// 初始化一个<see cref="RabbitMQEventBus"/>类型的新实例
    /// </summary>
    public RabbitMQEventBus(
        IConnection connection,
        ILogger<RabbitMQEventBus> logger,
        IServiceProvider serviceProvider,
        RabbitMQOptions options,
        string? exchangeName = null)
    {
        _connection = Check.NotNull(connection);
        _logger = Check.NotNull(logger);
        _serviceProvider = Check.NotNull(serviceProvider);
        _options = Check.NotNull(options);
        _exchangeName = exchangeName ?? "Tnzi.Events";
    }

    /// <summary>
    /// 延迟初始化 channel 和 exchange，避免在构造函数中使用 AsyncHelper.RunSync
    /// </summary>
    private async Task<IChannel> GetOrCreateChannelAsync()
    {
        if (_channelInitialized && _channel != null)
            return _channel;

        await _channelLock.WaitAsync();
        try
        {
            if (_channelInitialized && _channel != null)
                return _channel;

            _channel = await _connection.CreateChannelAsync();
            await _channel.ExchangeDeclareAsync(_exchangeName, ExchangeType.Topic, true, false);

            // 声明死信交换机
            await _channel.ExchangeDeclareAsync(_options.DeadLetterExchange, ExchangeType.Topic, true, false);

            _channelInitialized = true;
            return _channel;
        }
        finally
        {
            _channelLock.Release();
        }
    }

    /// <summary>
    /// 发布事件（使用 SemaphoreSlim 保护 channel 的线程安全）
    /// </summary>
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        Check.NotNull(@event);

        var eventType = typeof(TEvent);
        var eventTypeName = eventType.FullName ?? eventType.Name;
        var routingKey = eventTypeName;

        await _channelLock.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetOrCreateChannelAsync();

            // 序列化事件
            var json = JsonSerializer.Serialize(@event, TnziJsonDefaults.Options);
            var body = Encoding.UTF8.GetBytes(json);

            // 发布消息
            var properties = new BasicProperties
            {
                Persistent = true,
                MessageId = @event.EventId.ToString(),
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Type = eventTypeName
            };

            await channel.BasicPublishAsync(_exchangeName, routingKey, false, properties, body);

            _logger.LogDebug("Published event {EventType} with ID {EventId} to RabbitMQ", eventTypeName, @event.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event {EventType} to RabbitMQ", eventTypeName);
            throw;
        }
        finally
        {
            _channelLock.Release();
        }
    }

    /// <summary>
    /// 延迟发布事件
    /// 注意：当前使用 Task.Delay 实现延迟，进程重启会丢失延迟消息。
    /// 生产环境建议使用 RabbitMQ 延迟消息插件 (rabbitmq_delayed_message_exchange)
    /// </summary>
    public async Task PublishDelayedAsync<TEvent>(TEvent @event, TimeSpan delay, CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        if (delay > TimeSpan.Zero)
        {
            _logger.LogDebug(
                "Delaying event {EventType} by {Delay}. Note: in-process delay will be lost on restart",
                typeof(TEvent).Name, delay);
            await Task.Delay(delay, cancellationToken);
        }

        await PublishAsync(@event, cancellationToken);
    }

    /// <summary>
    /// 订阅事件（RabbitMQ特有方法，用于手动订阅）
    /// </summary>
    public async Task SubscribeEventAsync<TEvent>() where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent);
        var eventTypeName = eventType.FullName ?? eventType.Name;
        var queueName = $"Tnzi.Events.{eventTypeName}";
        var deadLetterQueueName = $"Tnzi.Events.DeadLetter.{eventTypeName}";

        if (_queueNames.TryGetValue(eventType, out _))
        {
            _logger.LogWarning("Event {EventType} is already subscribed", eventTypeName);
            return;
        }

        try
        {
            var channel = await GetOrCreateChannelAsync();

            // 设置 prefetchCount 控制消费者并发
            await channel.BasicQosAsync(0, _options.PrefetchCount, false);

            // 声明死信队列
            await channel.QueueDeclareAsync(deadLetterQueueName, true, false, false, null);
            await channel.QueueBindAsync(deadLetterQueueName, _options.DeadLetterExchange, eventTypeName);

            // 声明主队列，设置死信交换机参数
            var queueArgs = new Dictionary<string, object?>
            {
                { "x-dead-letter-exchange", _options.DeadLetterExchange },
                { "x-dead-letter-routing-key", eventTypeName }
            };

            await channel.QueueDeclareAsync(queueName, true, false, false, queueArgs);

            // 绑定队列到交换机
            await channel.QueueBindAsync(queueName, _exchangeName, eventTypeName);

            // 创建消费者
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (model, ea) =>
            {
                using var handlerScope = _serviceProvider.CreateScope();
                try
                {
                    var body = ea.Body.ToArray();
                    var json = Encoding.UTF8.GetString(body);
                    var @event = JsonSerializer.Deserialize<TEvent>(json, TnziJsonDefaults.Options);

                    if (@event == null)
                    {
                        _logger.LogWarning("Failed to deserialize event {EventType}", eventTypeName);
                        await AckWithChannelLockAsync(ea.DeliveryTag, false);
                        return;
                    }

                    // 获取事件处理器并执行
                    var handlers = GetEventHandlers<TEvent>(eventType, handlerScope.ServiceProvider);

                    var tasks = new List<Task>();
                    foreach (var handler in handlers)
                    {
                        if (handler == null) continue;
                        var handlerTask = ExecuteHandlerWithErrorIsolationAsync(handler, @event, eventType);
                        tasks.Add(handlerTask);
                    }

                    if (tasks.Count > 0)
                    {
                        await Task.WhenAll(tasks);
                    }

                    // 确认消息已处理
                    await AckWithChannelLockAsync(ea.DeliveryTag, false);

                    _logger.LogDebug("Processed event {EventType} with ID {EventId}", eventTypeName, @event.EventId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing event {EventType}", eventTypeName);

                    // 检查重试次数，超过最大次数后不再 requeue（由死信交换机处理）
                    var retryCount = GetRetryCount(ea.BasicProperties);
                    if (retryCount >= _options.MaxRetryCount)
                    {
                        _logger.LogWarning(
                            "Event {EventType} exceeded max retry count ({MaxRetryCount}), sending to dead letter queue",
                            eventTypeName, _options.MaxRetryCount);
                        // requeue=false，消息会被发送到死信交换机
                        await NackWithChannelLockAsync(ea.DeliveryTag, false, false);
                    }
                    else
                    {
                        // 重新入队前更新重试次数（通过 header）
                        // 注意：BasicNack + requeue=true 会保留原始 header，
                        // 但 RabbitMQ 不允许修改已入队的消息 header。
                        // 因此使用 reject + republish 模式来追踪重试次数。
                        await NackWithChannelLockAsync(ea.DeliveryTag, false, false);

                        // 重新发布消息并递增重试计数
                        try
                        {
                            var newProperties = new BasicProperties
                            {
                                Persistent = ea.BasicProperties.Persistent,
                                MessageId = ea.BasicProperties.MessageId,
                                Timestamp = ea.BasicProperties.Timestamp,
                                Type = ea.BasicProperties.Type,
                                Headers = ea.BasicProperties.Headers != null
                                    ? new Dictionary<string, object?>(ea.BasicProperties.Headers)
                                    : new Dictionary<string, object?>()
                            };
                            newProperties.Headers["x-retry-count"] = retryCount + 1;

                            await _channelLock.WaitAsync();
                            try
                            {
                                var ch = await GetOrCreateChannelAsync();
                                await ch.BasicPublishAsync(_exchangeName, eventTypeName, false, newProperties, ea.Body);
                            }
                            finally
                            {
                                _channelLock.Release();
                            }
                        }
                        catch (Exception republishEx)
                        {
                            _logger.LogError(republishEx, "Failed to republish event {EventType} for retry", eventTypeName);
                        }
                    }
                }
            };

            // 开始消费
            await _channelLock.WaitAsync();
            try
            {
                await channel.BasicConsumeAsync(queueName, false, consumer);
            }
            finally
            {
                _channelLock.Release();
            }

            _queueNames.TryAdd(eventType, queueName);
            _logger.LogInformation("Subscribed to event {EventType} on queue {QueueName}", eventTypeName, queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to event {EventType}", eventTypeName);
            throw;
        }
    }

    /// <summary>
    /// 从消息 header 中获取重试次数
    /// </summary>
    private static int GetRetryCount(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers == null)
            return 0;

        if (properties.Headers.TryGetValue("x-retry-count", out var value))
        {
            return value switch
            {
                int intVal => intVal,
                long longVal => (int)longVal,
                byte[] bytes => int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) ? parsed : 0,
                _ => 0
            };
        }

        return 0;
    }

    /// <summary>
    /// 线程安全的 BasicAck
    /// </summary>
    private async Task AckWithChannelLockAsync(ulong deliveryTag, bool multiple)
    {
        await _channelLock.WaitAsync();
        try
        {
            if (_channel != null)
                await _channel.BasicAckAsync(deliveryTag, multiple);
        }
        finally
        {
            _channelLock.Release();
        }
    }

    /// <summary>
    /// 线程安全的 BasicNack
    /// </summary>
    private async Task NackWithChannelLockAsync(ulong deliveryTag, bool multiple, bool requeue)
    {
        await _channelLock.WaitAsync();
        try
        {
            if (_channel != null)
                await _channel.BasicNackAsync(deliveryTag, multiple, requeue);
        }
        finally
        {
            _channelLock.Release();
        }
    }

    /// <summary>
    /// 获取事件的所有处理器（支持事件继承）
    /// </summary>
    private IEnumerable<object> GetEventHandlers<TEvent>(Type eventType, IServiceProvider serviceProvider) where TEvent : class, IEvent
    {
        var allHandlers = new List<object>();
        var handlerTypes = new HashSet<Type>();

        // 1. 从DI容器获取直接匹配的处理器
        var handlerInterface = typeof(IEventHandler<>).MakeGenericType(eventType);
        var directHandlers = serviceProvider.GetServices(handlerInterface);
        foreach (var handler in directHandlers)
        {
            if (handler != null)
            {
                allHandlers.Add(handler);
                handlerTypes.Add(handler.GetType());
            }
        }

        // 2. 获取基类事件的处理器（事件继承支持）
        var baseEventHandlers = GetBaseEventHandlers<TEvent>(eventType, serviceProvider);
        foreach (var handler in baseEventHandlers)
        {
            if (handler != null && !handlerTypes.Contains(handler.GetType()))
            {
                allHandlers.Add(handler);
                handlerTypes.Add(handler.GetType());
            }
        }

        return allHandlers;
    }

    /// <summary>
    /// 获取基类事件的处理器（支持事件继承）
    /// </summary>
    private IEnumerable<object> GetBaseEventHandlers<TEvent>(Type eventType, IServiceProvider serviceProvider) where TEvent : class, IEvent
    {
        var handlers = new List<object>();

        var baseInterfaces = EventHandlerInvoker.GetBaseHandlerInterfaces(eventType);
        foreach (var @interface in baseInterfaces)
        {
            var baseHandlers = serviceProvider.GetServices(@interface);
            foreach (var handler in baseHandlers)
            {
                if (handler != null)
                {
                    handlers.Add(handler);
                }
            }
        }

        return handlers;
    }

    /// <summary>
    /// 执行处理器并实现错误隔离（单个处理器失败不影响其他处理器）
    /// </summary>
    private async Task ExecuteHandlerWithErrorIsolationAsync<TEvent>(object handler, TEvent @event, Type eventType) where TEvent : class, IEvent
    {
        var handlerType = handler.GetType();
        try
        {
            // 获取或创建处理器元数据（编译委托缓存）
            var metadata = EventHandlerInvoker.GetMetadata(handlerType);

            // 检查条件处理器
            if (metadata.CanHandleDelegate != null)
            {
                if (!metadata.CanHandleDelegate(handler, @event))
                {
                    _logger.LogDebug("Handler {HandlerType} cannot handle event {EventType} (EventId: {EventId})",
                        handlerType.Name, eventType.Name, @event.EventId);
                    return;
                }
            }

            // 执行 HandleAsync
            if (metadata.HandleDelegate != null)
            {
                await metadata.HandleDelegate(handler, @event, CancellationToken.None);
            }
            else
            {
                _logger.LogWarning("Handler {HandlerType} does not have HandleAsync method with expected signature",
                    handlerType.Name);
            }
        }
        catch (Exception ex)
        {
            // 错误隔离：记录错误但不影响其他处理器
            _logger.LogError(ex,
                "Error in handler {HandlerType} for event {EventType} (EventId: {EventId}). " +
                "This error is isolated and will not affect other handlers.",
                handlerType.Name, eventType.Name, @event.EventId);
        }
    }

    public bool HasHandlers<TEvent>() where TEvent : class, IEvent
    {
        return GetHandlerCount<TEvent>() > 0;
    }

    public int GetHandlerCount<TEvent>() where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent);
        var handlerTypes = new HashSet<Type>();

        using var scope = _serviceProvider.CreateScope();

        // 检查DI容器注册的处理器
        var handlerInterface = typeof(IEventHandler<>).MakeGenericType(eventType);
        var diHandlers = scope.ServiceProvider.GetServices(handlerInterface);
        foreach (var handler in diHandlers)
        {
            if (handler != null)
                handlerTypes.Add(handler.GetType());
        }

        // 检查基类事件的处理器（去重）
        var baseHandlers = GetBaseEventHandlers<TEvent>(eventType, scope.ServiceProvider);
        foreach (var handler in baseHandlers)
        {
            if (handler != null)
                handlerTypes.Add(handler.GetType());
        }

        return handlerTypes.Count;
    }

    public void Subscribe<TEvent, THandler>()
        where TEvent : class, IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        _logger.LogWarning("Runtime subscription is not supported for RabbitMQEventBus. " +
                          "Use SubscribeEventAsync<TEvent>() method instead.");
    }

    // IIntegrationEventBus implementation
    Task IIntegrationEventBus.PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
    {
        return PublishAsync(@event, cancellationToken);
    }

    void IIntegrationEventBus.Subscribe<TEvent, THandler>()
    {
        _logger.LogWarning("IIntegrationEventBus.Subscribe is not supported for RabbitMQEventBus. " +
                          "Use SubscribeEventAsync<TEvent>() method instead, or register handlers in DI container.");
    }

    public void Unsubscribe<TEvent, THandler>()
        where TEvent : class, IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        _logger.LogWarning("Runtime unsubscription is not supported for RabbitMQEventBus.");
    }

    public void UnsubscribeAll<TEvent>() where TEvent : class, IEvent
    {
        _logger.LogWarning("Runtime unsubscription is not supported for RabbitMQEventBus.");
    }

    /// <summary>
    /// 释放资源
    /// 注意：不释放从 DI 注入的 _connection，由模块的 OnApplicationShutdownAsync 负责
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_channel != null)
            {
                try
                {
                    ((IDisposable)_channel).Dispose();
                }
                catch
                {
                    // Dispose 方法不应该抛出异常
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing RabbitMQ channel");
        }

        _channelLock.Dispose();

        // 不释放 _connection，由模块的 OnApplicationShutdownAsync 负责
    }
}
