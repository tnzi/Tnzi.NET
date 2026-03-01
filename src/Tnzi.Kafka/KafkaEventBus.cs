
namespace Tnzi.Kafka;

/// <summary>
/// Kafka事件总线实现
/// 同时实现IEventBus和IIntegrationEventBus接口
/// 实现IAsyncDisposable以支持消费者任务的优雅关闭
/// </summary>
public class KafkaEventBus : IEventBus, IIntegrationEventBus, IAsyncDisposable, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaEventBus> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly KafkaOptions _options;
    private readonly string _bootstrapServers;
    private readonly ConcurrentDictionary<Type, IConsumer<string, string>> _consumers = new();
    private readonly ConcurrentDictionary<Type, CancellationTokenSource> _consumerCancellationTokens = new();
    private readonly ConcurrentBag<Task> _consumerTasks = new();
    private bool _disposed;

    /// <inheritdoc />
    public bool IsLocal => false;

    /// <summary>
    /// 初始化一个<see cref="KafkaEventBus"/>类型的新实例
    /// </summary>
    public KafkaEventBus(
        IProducer<string, string> producer,
        ILogger<KafkaEventBus> logger,
        IServiceProvider serviceProvider,
        KafkaOptions options,
        string bootstrapServers)
    {
        _producer = Check.NotNull(producer);
        _logger = Check.NotNull(logger);
        _serviceProvider = Check.NotNull(serviceProvider);
        _options = Check.NotNull(options);
        _bootstrapServers = Check.NotNullOrWhiteSpace(bootstrapServers);
    }

    /// <summary>
    /// 发布事件
    /// </summary>
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        Check.NotNull(@event);

        var eventType = typeof(TEvent);
        var eventTypeName = eventType.FullName ?? eventType.Name;
        var topic = $"{_options.TopicPrefix}.{eventTypeName}";

        try
        {
            // 序列化事件
            var json = JsonSerializer.Serialize(@event, TnziJsonDefaults.Options);

            // 发布消息
            var message = new Message<string, string>
            {
                Key = @event.EventId.ToString(),
                Value = json,
                Headers = new Headers
                {
                    { "EventType", Encoding.UTF8.GetBytes(eventTypeName) },
                    { "EventTime", Encoding.UTF8.GetBytes(@event.EventTime.ToString("O")) }
                }
            };

            await _producer.ProduceAsync(topic, message, cancellationToken);

            _logger.LogDebug("Published event {EventType} with ID {EventId} to Kafka topic {Topic}",
                eventTypeName, @event.EventId, topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event {EventType} to Kafka", eventTypeName);
            throw;
        }
    }

    /// <summary>
    /// 延迟发布事件
    /// </summary>
    public async Task PublishDelayedAsync<TEvent>(TEvent @event, TimeSpan delay, CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }

        await PublishAsync(@event, cancellationToken);
    }

    /// <summary>
    /// 订阅事件（Kafka特有方法，用于手动订阅）
    /// </summary>
    public void SubscribeEvent<TEvent>() where TEvent : class, IEvent
    {
        var eventType = typeof(TEvent);
        var eventTypeName = eventType.FullName ?? eventType.Name;
        var topic = $"{_options.TopicPrefix}.{eventTypeName}";
        var groupId = $"{_options.GroupIdPrefix}.{eventTypeName}";

        if (_consumers.ContainsKey(eventType) || _consumerCancellationTokens.ContainsKey(eventType))
        {
            _logger.LogWarning("Event {EventType} is already subscribed", eventTypeName);
            return;
        }

        try
        {
            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = _bootstrapServers,
                GroupId = groupId,
                AutoOffsetReset = _options.Consumer.AutoOffsetReset,
                EnableAutoCommit = _options.Consumer.EnableAutoCommit,
                SessionTimeoutMs = _options.Consumer.SessionTimeoutMs
            };

            var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
            consumer.Subscribe(topic);

            // 启动后台任务消费消息，并跟踪任务以支持优雅关闭
            var cts = new CancellationTokenSource();
            // 使用 LongRunning 创建专用线程，避免阻塞线程池
            // Confluent.Kafka 的 Consume() 是阻塞 API，会永久占用线程
            var consumerTask = Task.Factory.StartNew(async () =>
            {
                var currentConsumer = consumer;
                var reconnectAttempts = 0;

                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        // 重连成功后重置计数器
                        reconnectAttempts = 0;

                        while (!cts.Token.IsCancellationRequested)
                        {
                            try
                            {
                                var result = currentConsumer.Consume(TimeSpan.FromSeconds(1));
                                if (result == null || result.Message == null)
                                {
                                    await Task.Delay(100, cts.Token);
                                    continue;
                                }

                                var json = result.Message.Value;
                                var @event = JsonSerializer.Deserialize<TEvent>(json, TnziJsonDefaults.Options);

                                if (@event == null)
                                {
                                    _logger.LogWarning("Failed to deserialize event {EventType}", eventTypeName);
                                    currentConsumer.Commit(result);
                                    continue;
                                }

                                // 获取事件处理器并执行（创建 Scope 以支持 Scoped 生命周期的处理器）
                                // 支持事件继承和条件处理器
                                using var handlerScope = _serviceProvider.CreateScope();
                                var handlers = GetEventHandlers<TEvent>(eventType, handlerScope.ServiceProvider);

                                var tasks = new List<Task>();
                                foreach (var handler in handlers)
                                {
                                    if (handler == null) continue;

                                    // 为每个处理器创建独立任务，实现错误隔离
                                    var handlerTask = ExecuteHandlerWithErrorIsolationAsync(handler, @event, eventType);
                                    tasks.Add(handlerTask);
                                }

                                if (tasks.Count > 0)
                                {
                                    // 等待所有处理器完成（即使某些失败，其他处理器也会继续执行）
                                    await Task.WhenAll(tasks);
                                }

                                // 提交偏移量
                                currentConsumer.Commit(result);

                                _logger.LogDebug("Processed event {EventType} with ID {EventId}", eventTypeName, @event.EventId);
                            }
                            catch (ConsumeException ex)
                            {
                                _logger.LogError(ex, "Error consuming event {EventType} from Kafka", eventTypeName);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常取消，退出循环
                        break;
                    }
                    catch (Exception ex)
                    {
                        reconnectAttempts++;

                        var maxAttempts = _options.Consumer.MaxReconnectAttempts;
                        if (maxAttempts <= 0 || reconnectAttempts > maxAttempts)
                        {
                            _logger.LogError(ex,
                                "Kafka consumer for event {EventType} has exhausted all {MaxAttempts} reconnect attempts. Consumer will stop.",
                                eventTypeName, maxAttempts);
                            break;
                        }

                        // 指数退避：1s, 2s, 4s, 8s... 最大 MaxReconnectBackoffSeconds
                        var backoffSeconds = Math.Min(
                            _options.Consumer.InitialReconnectBackoffSeconds * (int)Math.Pow(2, reconnectAttempts - 1),
                            _options.Consumer.MaxReconnectBackoffSeconds);

                        _logger.LogWarning(ex,
                            "Error in Kafka consumer for event {EventType}. Attempting reconnect {Attempt}/{MaxAttempts} after {Backoff}s delay.",
                            eventTypeName, reconnectAttempts, maxAttempts, backoffSeconds);

                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        // 尝试重建消费者
                        try
                        {
                            try
                            {
                                currentConsumer.Close();
                                currentConsumer.Dispose();
                            }
                            catch
                            {
                                // 忽略旧消费者清理错误
                            }

                            var newConsumerConfig = new ConsumerConfig
                            {
                                BootstrapServers = _bootstrapServers,
                                GroupId = groupId,
                                AutoOffsetReset = _options.Consumer.AutoOffsetReset,
                                EnableAutoCommit = _options.Consumer.EnableAutoCommit,
                                SessionTimeoutMs = _options.Consumer.SessionTimeoutMs
                            };

                            currentConsumer = new ConsumerBuilder<string, string>(newConsumerConfig).Build();
                            currentConsumer.Subscribe(topic);

                            // 更新引用
                            _consumers[eventType] = currentConsumer;

                            _logger.LogInformation(
                                "Kafka consumer for event {EventType} reconnected successfully (attempt {Attempt}).",
                                eventTypeName, reconnectAttempts);
                        }
                        catch (Exception reconnectEx)
                        {
                            _logger.LogError(reconnectEx,
                                "Failed to reconnect Kafka consumer for event {EventType} (attempt {Attempt}/{MaxAttempts}).",
                                eventTypeName, reconnectAttempts, maxAttempts);
                        }
                    }
                }
            }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

            // 跟踪消费者任务以支持优雅关闭
            _consumerTasks.Add(consumerTask);

            // 保存CancellationTokenSource以便后续取消
            _consumerCancellationTokens[eventType] = cts;

            _consumers[eventType] = consumer;

            _logger.LogInformation("Subscribed to event {EventType} on Kafka topic {Topic}", eventTypeName, topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to event {EventType}", eventTypeName);
            throw;
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
        _logger.LogWarning("Runtime subscription is not supported for KafkaEventBus. " +
                          "Use SubscribeEvent<TEvent>() method instead.");
    }

    public void Unsubscribe<TEvent, THandler>()
        where TEvent : class, IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        _logger.LogWarning("Runtime unsubscription is not supported for KafkaEventBus.");
    }

    public void UnsubscribeAll<TEvent>() where TEvent : class, IEvent
    {
        _logger.LogWarning("Runtime unsubscription is not supported for KafkaEventBus.");
    }

    // IIntegrationEventBus implementation
    Task IIntegrationEventBus.PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
    {
        // IIntegrationEvent继承自IEvent，所以可以直接调用IEventBus的PublishAsync方法
        return PublishAsync(@event, cancellationToken);
    }

    void IIntegrationEventBus.Subscribe<TEvent, THandler>()
    {
        // IIntegrationEventBus的Subscribe方法要求TEvent是IIntegrationEvent
        // KafkaEventBus使用SubscribeEvent来订阅事件，而不是运行时订阅
        // 这个方法主要用于声明订阅关系，实际订阅需要通过SubscribeEvent完成
        _logger.LogWarning("IIntegrationEventBus.Subscribe is not supported for KafkaEventBus. " +
                          "Use SubscribeEvent<TEvent>() method instead, or register handlers in DI container.");
    }

    /// <summary>
    /// 异步释放资源，支持消费者任务的优雅关闭
    /// 取消所有消费者任务并等待其完成（超时10秒保护）
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        // 取消所有消费者任务
        foreach (var cts in _consumerCancellationTokens.Values)
        {
            try { cts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        // 等待所有消费者任务完成，超时10秒保护
        if (!_consumerTasks.IsEmpty)
        {
            var allTasksCompletion = Task.WhenAll(_consumerTasks);
            var completedTask = await Task.WhenAny(allTasksCompletion, Task.Delay(TimeSpan.FromSeconds(10)));

            if (completedTask != allTasksCompletion)
            {
                _logger.LogWarning(
                    "Kafka consumer tasks did not complete within 10 seconds timeout. " +
                    "Forcing shutdown with {PendingCount} tasks still running.",
                    _consumerTasks.Count(t => !t.IsCompleted));
            }
        }

        // 委托给同步 Dispose 完成剩余清理
        Dispose();
    }

    /// <summary>
    /// 同步释放资源（直接同步清理，避免 DisposeAsync().GetResult() 死锁）
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // 取消所有消费者任务
        foreach (var cts in _consumerCancellationTokens.Values)
        {
            try { cts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        // 释放 CancellationTokenSource
        foreach (var cts in _consumerCancellationTokens.Values)
        {
            try { cts?.Dispose(); }
            catch { }
        }
        _consumerCancellationTokens.Clear();

        // 关闭所有消费者
        foreach (var consumer in _consumers.Values)
        {
            try
            {
                consumer?.Close();
                consumer?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing Kafka consumer");
            }
        }
        _consumers.Clear();

        // 释放生产者
        try
        {
            _producer?.Flush(TimeSpan.FromSeconds(5));
            _producer?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing Kafka producer");
        }

        GC.SuppressFinalize(this);
    }
}