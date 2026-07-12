

namespace Tnzi.RabbitMQ;

/// <summary>
/// RabbitMQ事件总线实现(分布式)
/// 实现 IDistributedEventBus 与 IIntegrationEventBus,PublishAsync 完成仅代表投递成功,
/// 不执行本进程内处理器;不替换 IEventBus(本地总线始终可用)
/// 使用独立的发布 Channel 和 per-consumer Channel 架构（RabbitMQ 最佳实践）
/// RabbitMQ.Client 7.x 的 IChannel 是线程安全的，发布操作无需外部锁
/// </summary>
public class RabbitMQEventBus : IDistributedEventBus, IIntegrationEventBus, IAsyncDisposable, IDisposable
{
    private readonly IConnection _connection;
    private readonly ILogger<RabbitMQEventBus> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly RabbitMQOptions _options;
    private readonly string _exchangeName;

    // 发布 Channel（延迟初始化，自动恢复）
    // volatile 确保多线程下 IsOpen 检查的可见性
    private volatile IChannel? _publishChannel;
    private readonly SemaphoreSlim _publishChannelLock = new(1, 1);

    // Channel pool for high-throughput publish scenarios
    private readonly ConcurrentBag<IChannel>? _channelPool;
    private readonly SemaphoreSlim? _channelPoolSemaphore;
    private int _channelPoolCount;

    // 每个事件类型的独立消费者 Channel（消除 publish/consume 锁竞争）
    private readonly ConcurrentDictionary<Type, ConsumerSubscription> _subscriptions = new();

    private volatile bool _disposed;

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

        // Initialize channel pool if enabled
        if (_options.ChannelPool is { Enabled: true })
        {
            _channelPool = new ConcurrentBag<IChannel>();
            _channelPoolSemaphore = new SemaphoreSlim(_options.ChannelPool.MaxSize, _options.ChannelPool.MaxSize);
            _logger.LogInformation("Channel pool enabled with max size {MaxSize}", _options.ChannelPool.MaxSize);
        }
    }

    /// <summary>
    /// 获取或创建发布 Channel（带自动恢复）
    /// SemaphoreSlim 仅保护 Channel 初始化/恢复路径，不保护每次发布操作
    /// </summary>
    private async Task<IChannel> GetPublishChannelAsync(CancellationToken cancellationToken = default)
    {
        // 快速路径：Channel 已初始化且健康
        var channel = _publishChannel;
        if (channel is { IsOpen: true })
            return channel;

        await _publishChannelLock.WaitAsync(cancellationToken);
        try
        {
            // 双重检查
            channel = _publishChannel;
            if (channel is { IsOpen: true })
                return channel;

            // 清理旧 Channel
            if (channel != null)
            {
                try { await channel.DisposeAsync(); }
                catch { /* ignore cleanup errors */ }
            }

            // 创建新 Channel 并声明交换机（幂等操作）
            var newChannel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await newChannel.ExchangeDeclareAsync(_exchangeName, ExchangeType.Topic, true, false, cancellationToken: cancellationToken);
            await newChannel.ExchangeDeclareAsync(_options.DeadLetterExchange, ExchangeType.Topic, true, false, cancellationToken: cancellationToken);

            _publishChannel = newChannel;
            _logger.LogDebug("Publish channel created/recovered");
            return newChannel;
        }
        finally
        {
            _publishChannelLock.Release();
        }
    }

    /// <summary>
    /// Acquire a channel from the pool, creating a new one if the pool has capacity.
    /// </summary>
    private async Task<IChannel> AcquirePooledChannelAsync(CancellationToken cancellationToken = default)
    {
        await _channelPoolSemaphore!.WaitAsync(cancellationToken);

        // Try to get an existing healthy channel from the pool
        while (_channelPool!.TryTake(out var channel))
        {
            if (channel.IsOpen)
                return channel;

            // Channel is dead, dispose and decrement count
            try { await channel.DisposeAsync(); }
            catch { /* ignore cleanup errors */ }
            Interlocked.Decrement(ref _channelPoolCount);
        }

        // No available channel, create a new one
        var newChannel = await CreateAndInitializeChannelAsync(cancellationToken);
        Interlocked.Increment(ref _channelPoolCount);
        return newChannel;
    }

    /// <summary>
    /// Return a channel to the pool for reuse.
    /// </summary>
    private void ReturnPooledChannel(IChannel channel)
    {
        if (channel.IsOpen && !_disposed)
        {
            _channelPool!.Add(channel);
        }
        else
        {
            try { channel.Dispose(); }
            catch { /* ignore cleanup errors */ }
            Interlocked.Decrement(ref _channelPoolCount);
        }

        _channelPoolSemaphore!.Release();
    }

    /// <summary>
    /// Create a new channel and declare required exchanges.
    /// </summary>
    private async Task<IChannel> CreateAndInitializeChannelAsync(CancellationToken cancellationToken = default)
    {
        var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(_exchangeName, ExchangeType.Topic, true, false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(_options.DeadLetterExchange, ExchangeType.Topic, true, false, cancellationToken: cancellationToken);
        return channel;
    }

    /// <summary>
    /// Publish a message to a specific channel.
    /// </summary>
    private async Task PublishToChannelAsync(IChannel channel, string eventTypeName, BasicProperties properties, byte[] body, CancellationToken cancellationToken)
    {
        await channel.BasicPublishAsync(_exchangeName, eventTypeName, false, properties, body, cancellationToken);
    }

    /// <summary>
    /// 发布事件（使用独立的发布 Channel 或 Channel 池，IChannel v7.x 线程安全）
    /// </summary>
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Check.NotNull(@event);

        var eventType = typeof(TEvent);
        var eventTypeName = eventType.FullName ?? eventType.Name;

        try
        {
            var json = JsonSerializer.Serialize(@event, TnziJsonDefaults.Options);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = new BasicProperties
            {
                Persistent = true,
                MessageId = @event.EventId.ToString(),
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Type = eventTypeName
            };

            // Use channel pool if enabled, otherwise use single publish channel
            if (_channelPool != null)
            {
                var channel = await AcquirePooledChannelAsync(cancellationToken);
                try
                {
                    await PublishToChannelAsync(channel, eventTypeName, properties, body, cancellationToken);
                }
                finally
                {
                    ReturnPooledChannel(channel);
                }
            }
            else
            {
                var channel = await GetPublishChannelAsync(cancellationToken);
                await PublishToChannelAsync(channel, eventTypeName, properties, body, cancellationToken);
            }

            _logger.LogDebug("Published event {EventType} with ID {EventId} to RabbitMQ", eventTypeName, @event.EventId);
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            _logger.LogError(ex, "Failed to publish event {EventType} to RabbitMQ", eventTypeName);
            throw new RabbitMQException($"Failed to publish event {eventTypeName}", innerException: ex);
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (delay > TimeSpan.Zero)
        {
            _logger.LogWarning(
                "Delaying event {EventType} by {Delay} using in-process Task.Delay. " +
                "This delay will be lost if the process restarts. " +
                "For reliable delayed messaging, use the rabbitmq_delayed_message_exchange plugin.",
                typeof(TEvent).Name, delay);
            await Task.Delay(delay, cancellationToken);
        }

        await PublishAsync(@event, cancellationToken);
    }

    /// <summary>
    /// 订阅事件（每个订阅创建独立的 Channel，RabbitMQ 最佳实践）
    /// </summary>
    public async Task SubscribeEventAsync<TEvent>() where TEvent : class, IEvent
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var eventType = typeof(TEvent);
        var eventTypeName = eventType.FullName ?? eventType.Name;
        var queueName = $"Tnzi.Events.{eventTypeName}";
        var deadLetterQueueName = $"Tnzi.Events.DeadLetter.{eventTypeName}";

        // TryAdd 作为并发锁定，防止重复订阅
        var subscription = new ConsumerSubscription(queueName);
        if (!_subscriptions.TryAdd(eventType, subscription))
        {
            _logger.LogWarning("Event {EventType} is already subscribed", eventTypeName);
            return;
        }

        try
        {
            // 每个消费者创建独立的 Channel（消除 publish/consume 跨 Channel 锁竞争）
            var channel = await _connection.CreateChannelAsync();
            subscription.Channel = channel;

            // 声明交换机（幂等，确保消费者 Channel 可访问）
            await channel.ExchangeDeclareAsync(_exchangeName, ExchangeType.Topic, true, false);
            await channel.ExchangeDeclareAsync(_options.DeadLetterExchange, ExchangeType.Topic, true, false);

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
            await channel.QueueBindAsync(queueName, _exchangeName, eventTypeName);

            // 创建消费者
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                if (_disposed) return;

                using var handlerScope = _serviceProvider.CreateScope();
                try
                {
                    // 使用 Span 避免额外数组分配
                    var json = Encoding.UTF8.GetString(ea.Body.Span);
                    var @event = JsonSerializer.Deserialize<TEvent>(json, TnziJsonDefaults.Options);

                    if (@event == null)
                    {
                        _logger.LogWarning("Failed to deserialize event {EventType}", eventTypeName);
                        await channel.BasicAckAsync(ea.DeliveryTag, false);
                        return;
                    }

                    // 获取事件处理器并执行
                    var handlers = GetEventHandlers<TEvent>(eventType, handlerScope.ServiceProvider);
                    var tasks = new List<Task>();
                    foreach (var handler in handlers)
                    {
                        if (handler != null)
                            tasks.Add(ExecuteHandlerWithErrorIsolationAsync(handler, @event, eventType));
                    }

                    if (tasks.Count > 0)
                        await Task.WhenAll(tasks);

                    // 在消费者自己的 Channel 上 ACK（无跨 Channel 竞争）
                    await channel.BasicAckAsync(ea.DeliveryTag, false);

                    _logger.LogDebug("Processed event {EventType} with ID {EventId}", eventTypeName, @event.EventId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing event {EventType}", eventTypeName);
                    await HandleConsumerErrorAsync(channel, ea, eventTypeName);
                }
            };

            // 开始消费（在消费者自己的 Channel 上）
            await channel.BasicConsumeAsync(queueName, false, consumer);

            _logger.LogInformation("Subscribed to event {EventType} on queue {QueueName} (dedicated channel)",
                eventTypeName, queueName);
        }
        catch (Exception ex)
        {
            _subscriptions.TryRemove(eventType, out _);
            _logger.LogError(ex, "Failed to subscribe to event {EventType}", eventTypeName);
            throw new RabbitMQException($"Failed to subscribe to event {eventTypeName}", queueName, ex);
        }
    }

    /// <summary>
    /// 处理消费者错误：重试或发送到死信队列
    /// Nack 在消费者 Channel 上，republish 在发布 Channel 上（各自独立）
    /// </summary>
    private async Task HandleConsumerErrorAsync(IChannel consumerChannel, BasicDeliverEventArgs ea, string eventTypeName)
    {
        var retryCount = GetRetryCount(ea.BasicProperties);
        if (retryCount >= _options.MaxRetryCount)
        {
            _logger.LogWarning(
                "Event {EventType} exceeded max retry count ({MaxRetryCount}), sending to dead letter queue",
                eventTypeName, _options.MaxRetryCount);
            // requeue=false，消息会被发送到死信交换机
            await consumerChannel.BasicNackAsync(ea.DeliveryTag, false, false);
        }
        else
        {
            // Reject 在消费者 Channel 上
            await consumerChannel.BasicNackAsync(ea.DeliveryTag, false, false);

            // Apply exponential backoff delay before republishing
            var delay = _options.RetryDelay.GetDelay(retryCount + 1);
            if (delay > TimeSpan.Zero)
            {
                _logger.LogDebug("Applying retry delay of {DelayMs}ms for event {EventType} (attempt {RetryCount})",
                    delay.TotalMilliseconds, eventTypeName, retryCount + 1);
                await Task.Delay(delay);
            }

            // 重新发布消息并递增重试计数（使用发布 Channel，无锁竞争）
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

                var publishChannel = await GetPublishChannelAsync();
                await publishChannel.BasicPublishAsync(_exchangeName, eventTypeName, false, newProperties, ea.Body);
            }
            catch (Exception republishEx)
            {
                _logger.LogError(republishEx, "Failed to republish event {EventType} for retry", eventTypeName);
            }
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
    private static IEnumerable<object> GetBaseEventHandlers<TEvent>(Type eventType, IServiceProvider serviceProvider) where TEvent : class, IEvent
    {
        var handlers = new List<object>();

        var baseInterfaces = EventHandlerInvoker.GetBaseHandlerInterfaces(eventType);
        foreach (var @interface in baseInterfaces)
        {
            var baseHandlers = serviceProvider.GetServices(@interface);
            foreach (var handler in baseHandlers)
            {
                if (handler != null)
                    handlers.Add(handler);
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
            _logger.LogError(ex,
                "Error in handler {HandlerType} for event {EventType} (EventId: {EventId}). " +
                "This error is isolated and will not affect other handlers.",
                handlerType.Name, eventType.Name, @event.EventId);
        }
    }

    /// <inheritdoc />
    public bool HasHandlers<TEvent>() where TEvent : class, IEvent
    {
        return GetHandlerCount<TEvent>() > 0;
    }

    /// <inheritdoc />
    public int GetHandlerCount<TEvent>() where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent);
        var handlerTypes = new HashSet<Type>();

        using var scope = _serviceProvider.CreateScope();

        var handlerInterface = typeof(IEventHandler<>).MakeGenericType(eventType);
        var diHandlers = scope.ServiceProvider.GetServices(handlerInterface);
        foreach (var handler in diHandlers)
        {
            if (handler != null)
                handlerTypes.Add(handler.GetType());
        }

        var baseHandlers = GetBaseEventHandlers<TEvent>(eventType, scope.ServiceProvider);
        foreach (var handler in baseHandlers)
        {
            if (handler != null)
                handlerTypes.Add(handler.GetType());
        }

        return handlerTypes.Count;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public void Unsubscribe<TEvent, THandler>()
        where TEvent : class, IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        _logger.LogWarning("Runtime unsubscription is not supported for RabbitMQEventBus.");
    }

    /// <inheritdoc />
    public void UnsubscribeAll<TEvent>() where TEvent : class, IEvent
    {
        _logger.LogWarning("Runtime unsubscription is not supported for RabbitMQEventBus.");
    }

    /// <summary>
    /// 异步释放资源，优雅关闭所有 Channel
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // 关闭所有消费者 Channel
        foreach (var (eventType, subscription) in _subscriptions)
        {
            if (subscription.Channel != null)
            {
                try
                {
                    await subscription.Channel.CloseAsync();
                    await subscription.Channel.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing consumer channel for {EventType}", eventType.Name);
                }
            }
        }
        _subscriptions.Clear();

        // 关闭 Channel 池中的所有 Channel
        if (_channelPool != null)
        {
            while (_channelPool.TryTake(out var pooledChannel))
            {
                try
                {
                    await pooledChannel.CloseAsync();
                    await pooledChannel.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing pooled channel");
                }
            }
            _channelPoolSemaphore?.Dispose();
        }

        // 关闭发布 Channel
        var publishChannel = _publishChannel;
        if (publishChannel != null)
        {
            try
            {
                await publishChannel.CloseAsync();
                await publishChannel.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing publish channel");
            }
            _publishChannel = null;
        }

        _publishChannelLock.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 同步释放资源
    /// 注意：不释放从 DI 注入的 _connection，由模块的 OnApplicationShutdownAsync 负责
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var (_, subscription) in _subscriptions)
        {
            try { subscription.Channel?.Dispose(); }
            catch { /* Dispose 不应抛出异常 */ }
        }
        _subscriptions.Clear();

        // Dispose pooled channels
        if (_channelPool != null)
        {
            while (_channelPool.TryTake(out var pooledChannel))
            {
                try { pooledChannel.Dispose(); }
                catch { /* Dispose 不应抛出异常 */ }
            }
            _channelPoolSemaphore?.Dispose();
        }

        try { _publishChannel?.Dispose(); }
        catch { /* Dispose 不应抛出异常 */ }
        _publishChannel = null;

        _publishChannelLock.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 消费者订阅信息（每个事件类型一个独立 Channel）
    /// </summary>
    private sealed class ConsumerSubscription(string queueName)
    {
        public string QueueName { get; } = queueName;
        public IChannel? Channel { get; set; }
    }
}
