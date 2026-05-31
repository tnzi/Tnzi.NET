
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

                                // 执行处理器；失败时按重试预算在进程内重试，耗尽后进 DLQ 或保留偏移量等待重投。
                                // 关键不变量：处理器失败绝不无条件提交偏移量（杜绝静默丢消息）。
                                var failureCount = await RunHandlersAsync<TEvent>(eventType, @event);
                                var attemptsMade = 1;

                                while (true)
                                {
                                    var outcome = KafkaConsumeDecider.Decide(
                                        failureCount,
                                        attemptsMade,
                                        _options.Consumer.MaxConsumeRetries,
                                        _options.Consumer.DeadLetterEnabled);

                                    if (outcome == KafkaConsumeOutcome.Commit)
                                    {
                                        currentConsumer.Commit(result);
                                        _logger.LogDebug("Processed event {EventType} with ID {EventId}", eventTypeName, @event.EventId);
                                        break;
                                    }

                                    if (outcome == KafkaConsumeOutcome.Retry)
                                    {
                                        _logger.LogWarning(
                                            "{FailureCount} handler(s) failed for event {EventType} (EventId: {EventId}). Retrying in-process (attempt {Attempt}/{Max}).",
                                            failureCount, eventTypeName, @event.EventId, attemptsMade, _options.Consumer.MaxConsumeRetries);

                                        if (_options.Consumer.ConsumeRetryBackoffMs > 0)
                                        {
                                            await Task.Delay(_options.Consumer.ConsumeRetryBackoffMs, cts.Token);
                                        }

                                        failureCount = await RunHandlersAsync<TEvent>(eventType, @event);
                                        attemptsMade++;
                                        continue;
                                    }

                                    if (outcome == KafkaConsumeOutcome.DeadLetter)
                                    {
                                        try
                                        {
                                            await ProduceToDeadLetterAsync(topic, result.Message, eventTypeName, @event.EventId);
                                            currentConsumer.Commit(result);
                                            _logger.LogError(
                                                "Event {EventType} (EventId: {EventId}) routed to dead-letter topic after {Attempts} failed attempt(s); offset committed.",
                                                eventTypeName, @event.EventId, attemptsMade);
                                        }
                                        catch (Exception dlqEx)
                                        {
                                            // DLQ 投递失败 ⇒ 不提交偏移量，等待重投，绝不丢失
                                            _logger.LogCritical(dlqEx,
                                                "Failed to dead-letter event {EventType} (EventId: {EventId}); offset NOT committed, message will be redelivered.",
                                                eventTypeName, @event.EventId);
                                        }
                                        break;
                                    }

                                    // RedeliverWithoutCommit：不提交偏移量，等待重投（at-least-once，绝不静默丢弃）
                                    _logger.LogCritical(
                                        "Event {EventType} (EventId: {EventId}) failed after {Attempts} attempt(s) and dead-letter is disabled. " +
                                        "Offset NOT committed; message will be redelivered (at-least-once).",
                                        eventTypeName, @event.EventId, attemptsMade);
                                    break;
                                }
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
    /// 在独立 DI scope 内执行某事件的全部处理器，返回失败的处理器数量。
    /// 单个处理器失败不影响其他处理器（错误隔离），但失败会被计数以决定是否提交偏移量。
    /// </summary>
    private async Task<int> RunHandlersAsync<TEvent>(Type eventType, TEvent @event) where TEvent : class, IEvent
    {
        // 每次（含重试）使用全新 scope，确保 Scoped 处理器被重新解析
        using var handlerScope = _serviceProvider.CreateScope();
        var handlers = GetEventHandlers<TEvent>(eventType, handlerScope.ServiceProvider);

        var tasks = new List<Task<bool>>();
        foreach (var handler in handlers)
        {
            if (handler == null) continue;

            // 为每个处理器创建独立任务，实现错误隔离
            tasks.Add(ExecuteHandlerWithErrorIsolationAsync(handler, @event, eventType));
        }

        if (tasks.Count == 0)
        {
            return 0;
        }

        // 等待所有处理器完成（即使某些失败，其他处理器也会继续执行）
        var results = await Task.WhenAll(tasks);
        return results.Count(succeeded => !succeeded);
    }

    /// <summary>
    /// 执行处理器并实现错误隔离（单个处理器失败不影响其他处理器）。
    /// 返回 true 表示成功（含条件处理器主动跳过、缺失 HandleAsync 的配置型问题），false 表示执行抛异常。
    /// </summary>
    private async Task<bool> ExecuteHandlerWithErrorIsolationAsync<TEvent>(object handler, TEvent @event, Type eventType) where TEvent : class, IEvent
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
                    return true;
                }
            }

            // 执行 HandleAsync
            if (metadata.HandleDelegate != null)
            {
                await metadata.HandleDelegate(handler, @event, CancellationToken.None);
            }
            else
            {
                // 配置型问题（缺失 HandleAsync）：重试无益，记录告警但不计为可重试失败
                _logger.LogWarning("Handler {HandlerType} does not have HandleAsync method with expected signature",
                    handlerType.Name);
            }

            return true;
        }
        catch (Exception ex)
        {
            // 错误隔离：记录错误但不影响其他处理器；返回 false 以便上层据此决定是否提交偏移量
            _logger.LogError(ex,
                "Error in handler {HandlerType} for event {EventType} (EventId: {EventId}). " +
                "This error is isolated and will not affect other handlers.",
                handlerType.Name, eventType.Name, @event.EventId);
            return false;
        }
    }

    /// <summary>
    /// 将处理失败的原始消息投递到死信主题（"{源主题}{DeadLetterTopicSuffix}"），保留原始头并附加死信元数据。
    /// 投递失败时抛出，由调用方决定不提交偏移量（消息将重投，绝不丢失）。
    /// </summary>
    private async Task ProduceToDeadLetterAsync(string sourceTopic, Message<string, string> original, string eventTypeName, Guid eventId)
    {
        var dlqTopic = $"{sourceTopic}{_options.Consumer.DeadLetterTopicSuffix}";

        var headers = new Headers();
        if (original.Headers != null)
        {
            foreach (var header in original.Headers)
            {
                headers.Add(header.Key, header.GetValueBytes());
            }
        }
        headers.Add("x-dead-letter-source-topic", Encoding.UTF8.GetBytes(sourceTopic));
        headers.Add("x-dead-letter-event-type", Encoding.UTF8.GetBytes(eventTypeName));

        var dlqMessage = new Message<string, string>
        {
            Key = original.Key,
            Value = original.Value,
            Headers = headers
        };

        await _producer.ProduceAsync(dlqTopic, dlqMessage);
        _logger.LogWarning("Event {EventType} (EventId: {EventId}) produced to dead-letter topic {DlqTopic}.",
            eventTypeName, eventId, dlqTopic);
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