
namespace Tnzi.Kafka;

/// <summary>
/// Kafka事件总线实现(分布式)
/// 实现 IDistributedEventBus 与 IIntegrationEventBus,PublishAsync 完成仅代表投递成功,
/// 不执行本进程内处理器;不替换 IEventBus(本地总线始终可用)
/// 实现IAsyncDisposable以支持消费者任务的优雅关闭
/// </summary>
public class KafkaEventBus : IDistributedEventBus, IIntegrationEventBus, IDistributedEventSubscriber, IAsyncDisposable, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaEventBus> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly KafkaOptions _options;
    private readonly string _bootstrapServers;
    private readonly DistributedConsumerIdentity _consumerIdentity;
    private readonly ConcurrentDictionary<Type, IConsumer<string, string>> _consumers = new();
    private readonly ConcurrentDictionary<Type, CancellationTokenSource> _consumerCancellationTokens = new();
    private readonly ConcurrentBag<Task> _consumerTasks = new();
    private readonly Func<ConsumerConfig, IConsumer<string, string>> _consumerFactory;
    private readonly ConcurrentDictionary<string, byte> _stoppedConsumers = new();
    private bool _disposed;

    /// <summary>死信头 <c>x-dead-letter-reason</c>：处理器耗尽重试预算。</summary>
    internal const string DeadLetterReasonHandlerFailure = "handler-failure";

    /// <summary>死信头 <c>x-dead-letter-reason</c>：消息本身反序列化不出来（毒消息，重投无益）。</summary>
    internal const string DeadLetterReasonDeserialization = "deserialization";

    /// <summary>毒消息卡住分区时，每隔多少个轮询周期再记一次 Critical（默认退避 1 秒即约每分钟一条）。</summary>
    private const int PoisonStallLogEveryCycles = 60;

    /// <inheritdoc />
    public bool IsLocal => false;

    /// <summary>
    /// 本进程的消费者身份：消费者组以它的组名开头，广播事件的组再带上实例 ID。
    /// </summary>
    public DistributedConsumerIdentity ConsumerIdentity => _consumerIdentity;

    /// <summary>
    /// 消费循环已因耗尽重连预算而退出的事件类型（全名）。<see cref="KafkaHealthProbe"/> 据此把实例报成不就绪 ——
    /// 只问集群元数据的探针对「本进程某个事件类型已停止消费」一无所知，编排器会继续把流量导向一个不再消费的实例。
    /// 再次 <see cref="SubscribeEvent(Type)"/> 成功即从这里移除。
    /// </summary>
    public IReadOnlyCollection<string> StoppedConsumers => _stoppedConsumers.Keys.ToArray();

    /// <summary>
    /// 初始化一个<see cref="KafkaEventBus"/>类型的新实例
    /// </summary>
    /// <param name="producer">Kafka 生产者。</param>
    /// <param name="logger">日志。</param>
    /// <param name="serviceProvider">根服务提供者（处理器按消息各开一个作用域解析）。</param>
    /// <param name="options">Kafka 选项。</param>
    /// <param name="bootstrapServers">引导服务器地址。</param>
    /// <param name="consumerIdentity">
    /// 消费者身份（<c>EventBus:ConsumerGroup</c> + 实例 ID）；缺省按入口程序集名解析。
    /// 消费者组此前只由事件类型决定（<c>{GroupIdPrefix}.{事件}</c>），同一代理上所有 Tnzi 进程同组，
    /// 一个分区的主题上永远只有其中一个成员收到消息。
    /// </param>
    public KafkaEventBus(
        IProducer<string, string> producer,
        ILogger<KafkaEventBus> logger,
        IServiceProvider serviceProvider,
        KafkaOptions options,
        string bootstrapServers,
        DistributedConsumerIdentity? consumerIdentity = null)
        : this(producer, logger, serviceProvider, options, bootstrapServers, consumerIdentity, consumerFactory: null)
    {
    }

    /// <summary>
    /// 测试缝：消费循环里的消费者由 <paramref name="consumerFactory"/> 建出来（缺省 <see cref="ConsumerBuilder{TKey,TValue}"/>），
    /// 毒消息处置这类循环内的逻辑才能在没有代理的情况下被驱动。
    /// </summary>
    internal KafkaEventBus(
        IProducer<string, string> producer,
        ILogger<KafkaEventBus> logger,
        IServiceProvider serviceProvider,
        KafkaOptions options,
        string bootstrapServers,
        DistributedConsumerIdentity? consumerIdentity,
        Func<ConsumerConfig, IConsumer<string, string>>? consumerFactory)
    {
        _producer = Check.NotNull(producer);
        _logger = Check.NotNull(logger);
        _serviceProvider = Check.NotNull(serviceProvider);
        _options = Check.NotNull(options);
        _bootstrapServers = Check.NotNullOrWhiteSpace(bootstrapServers);
        _consumerIdentity = consumerIdentity ?? DistributedConsumerIdentity.FromOptions(new EventBusOptions());
        _consumerFactory = consumerFactory ?? (config => new ConsumerBuilder<string, string>(config).Build());
    }

    /// <summary>
    /// 按订阅描述构造消费者配置：组名 = <c>{GroupIdPrefix}.{ConsumerName}</c>，广播强制 <c>Latest</c>。
    /// </summary>
    /// <remarks>
    /// 广播的组带实例 ID，是一个全新的组：若沿用 <c>Earliest</c>，每个新起的实例都会把主题里
    /// 保留的全部历史广播重放一遍 —— 而一条过期的广播重放出来是有害的。
    /// 初次订阅与重连共用这一处，两条路径的配置不会再分叉。
    /// </remarks>
    internal ConsumerConfig BuildConsumerConfig(DistributedSubscription subscription)
    {
        Check.NotNull(subscription);

        return new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = $"{_options.GroupIdPrefix}.{subscription.ConsumerName}",
            AutoOffsetReset = subscription.IsBroadcast ? AutoOffsetReset.Latest : _options.Consumer.AutoOffsetReset,
            EnableAutoCommit = _options.Consumer.EnableAutoCommit,
            SessionTimeoutMs = _options.Consumer.SessionTimeoutMs
        };
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
            // 兜底捕获环境租户（经 PublishEventAsync 来的事件已在调用方作用域捕获过；
            // 这里服务的是直接注入本总线的调用方）。序列化之前不补，线上 JSON 的 TenantId 恒为 null
            EventTenantContext.CaptureInNewScope(@event, _serviceProvider);

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
        => SubscribeEvent(typeof(TEvent));

    /// <inheritdoc />
    /// <remarks>
    /// 非泛型入口，供 <see cref="DistributedEventSubscriptionInitializer"/> 按启动时发现的
    /// 事件类型逐个订阅。订阅本身是同步的（建消费者 + 起后台循环），因此这里不需要真异步。
    /// </remarks>
    public Task SubscribeEventAsync(Type eventType, CancellationToken cancellationToken = default)
    {
        SubscribeEvent(eventType);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 订阅事件（按运行时类型）。
    /// </summary>
    /// <remarks>
    /// 泛型重载只是这个方法的门面：消费循环真正需要的只有事件的具体类型（反序列化目标、
    /// 主题名、处理器接口的类型实参），泛型参数除约束外一处也没用到。
    /// </remarks>
    public void SubscribeEvent(Type eventType)
    {
        Check.NotNull(eventType);

        if (!typeof(IEvent).IsAssignableFrom(eventType) || !eventType.IsClass || eventType.IsAbstract)
        {
            throw new ArgumentException(
                $"Type '{eventType.FullName}' cannot be subscribed: it must be a non-abstract class implementing IEvent.",
                nameof(eventType));
        }

        // 消费者组由核心的 DistributedConsumerIdentity 决定，与 RabbitMQ 侧共用同一段命名代码：
        // 工作组 {前缀}.{组}.{事件}（同一服务的实例共用、代理分发），广播 {前缀}.{组}.{事件}.{实例}（每实例一组）。
        // 此前是 {前缀}.{事件}：同一代理上所有 Tnzi 进程同组，一个分区的主题上永远只有一个成员收到消息
        var subscription = _consumerIdentity.Describe(eventType);
        var eventTypeName = subscription.EventName;
        var topic = $"{_options.TopicPrefix}.{eventTypeName}";

        if (_consumers.ContainsKey(eventType) || _consumerCancellationTokens.ContainsKey(eventType))
        {
            // 启动期自动订阅与应用自己调用 Subscribe 会同时到达同一个类型，这是常态不是异常
            _logger.LogDebug("Event {EventType} is already subscribed; the request is a no-op", eventTypeName);
            return;
        }

        try
        {
            var consumerConfig = BuildConsumerConfig(subscription);

            var consumer = _consumerFactory(consumerConfig);
            consumer.Subscribe(topic);

            // 启动后台任务消费消息，并跟踪任务以支持优雅关闭
            var cts = new CancellationTokenSource();

            // ★ 先登记再起循环：循环可能在起来的几微秒内就因传输故障退出并把自己从登记表里摘掉
            // （MaxReconnectAttempts=0），登记若排在启动之后，会把一条死掉的登记写回去、并抹掉「已停止」标记
            _consumerCancellationTokens[eventType] = cts;
            _consumers[eventType] = consumer;
            _stoppedConsumers.TryRemove(eventTypeName, out _);

            // LongRunning 提示调度器不要占用线程池线程跑这个长驻循环
            // （委托是 async 的，第一次 await 之后的续体仍回到线程池；
            // Confluent.Kafka 的 Consume() 是阻塞 API，故循环体带 1 秒超时轮询）
            var consumerTask = Task.Factory.StartNew(async () =>
            {
                var currentConsumer = consumer;
                var reconnectAttempts = 0;
                var poisonStalls = new PoisonStallTracker();

                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        while (!cts.Token.IsCancellationRequested)
                        {
                            try
                            {
                                var result = currentConsumer.Consume(TimeSpan.FromSeconds(1));

                                // 一次成功的拉取即证明连接健康，此时才重置重连计数。
                                // 放在外层循环开头无条件清零会让 MaxReconnectAttempts
                                // 永远无法耗尽（每次失败都从 0 重新计数 → 无限重连）。
                                reconnectAttempts = 0;

                                if (result == null || result.Message == null)
                                {
                                    await Task.Delay(100, cts.Token);
                                    continue;
                                }

                                // ★ 反序列化不出来（tombstone / 未知枚举值 / 类型不匹配 / 载荷截断）是消息的问题，不是连接的问题。
                                // 它必须在这里被处置，绝不能穿到外层的「重连」处理器：那会关闭并重建消费者（每轮一次 rebalance）、
                                // 从同一偏移量再读到同一条、再抛 —— 无限循环，毒消息永远进不了 DLQ，而 /health/ready 照报健康。
                                var @event = TryDeserialize(result, eventType, out var poisonReason);
                                if (@event == null)
                                {
                                    var stallCycles = poisonStalls.Register(result.TopicPartitionOffset);
                                    await HandlePoisonMessageAsync(
                                        currentConsumer, result, topic, eventTypeName, consumerConfig.GroupId, subscription.IsBroadcast, poisonReason, stallCycles, cts.Token);
                                    continue;
                                }

                                // 只清本分区：别的分区送来好记录说明不了卡住的那条有任何变化（多分区主题上那会让节流失效）
                                poisonStalls.Clear(result.TopicPartition);

                                // 执行处理器；失败时按重试预算在进程内重试，耗尽后进 DLQ 或保留偏移量等待重投。
                                // 关键不变量：处理器失败绝不无条件提交偏移量（杜绝静默丢消息）。
                                var failureCount = await RunHandlersCountingFailuresAsync(eventType, @event);
                                var attemptsMade = 1;

                                while (true)
                                {
                                    var outcome = KafkaConsumeDecider.Decide(
                                        failureCount,
                                        attemptsMade,
                                        _options.Consumer.MaxConsumeRetries,
                                        _options.Consumer.DeadLetterEnabled,
                                        subscription.IsBroadcast);

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
                                            failureCount, eventTypeName, @event.EventId, attemptsMade,
                                            subscription.IsBroadcast ? 1 : _options.Consumer.MaxConsumeRetries);

                                        if (_options.Consumer.ConsumeRetryBackoffMs > 0)
                                        {
                                            await Task.Delay(_options.Consumer.ConsumeRetryBackoffMs, cts.Token);
                                        }

                                        failureCount = await RunHandlersCountingFailuresAsync(eventType, @event);
                                        attemptsMade++;
                                        continue;
                                    }

                                    if (outcome == KafkaConsumeOutcome.Drop)
                                    {
                                        // 广播：与 RabbitMQ 侧同一契约 —— 重投一次后丢弃，不进死信（过期的广播重放出来是有害的），
                                        // 也不保留偏移量（广播组按实例、随实例退出而废弃，没有下一个消费者会来重投）。
                                        currentConsumer.Commit(result);
                                        _logger.LogError(
                                            "Broadcast event {EventType} (EventId: {EventId}) failed again after one retry; dropping it. " +
                                            "Broadcast handlers must tolerate loss: the state must heal itself on the next read.",
                                            eventTypeName, @event.EventId);
                                        break;
                                    }

                                    if (outcome == KafkaConsumeOutcome.DeadLetter)
                                    {
                                        try
                                        {
                                            await ProduceToDeadLetterAsync(
                                                topic, result.Message, eventTypeName, @event.EventId, consumerConfig.GroupId, DeadLetterReasonHandlerFailure);
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
                                // 退避后再拉：持续性的消费错误（主题不存在、鉴权失败、分区不可用）
                                // 每次 Consume 都会立刻抛出而不等满轮询超时，没有退避就是一个
                                // 满速自旋 —— 烧 CPU、刷日志，而问题本身一条都修不了。
                                _logger.LogError(ex, "Error consuming event {EventType} from Kafka", eventTypeName);

                                if (_options.Consumer.ConsumeErrorBackoffMs > 0)
                                {
                                    await Task.Delay(_options.Consumer.ConsumeErrorBackoffMs, cts.Token);
                                }
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

                            // 从登记表里退出：留着一条死掉的登记会让之后任何一次 SubscribeEvent 都成为 no-op（「already subscribed」），
                            // 这个事件类型就再也没有人能把它救活
                            _consumers.TryRemove(eventType, out _);
                            _consumerCancellationTokens.TryRemove(eventType, out _);
                            _stoppedConsumers[eventTypeName] = 0;
                            try
                            {
                                currentConsumer.Close();
                                currentConsumer.Dispose();
                            }
                            catch (Exception disposeEx)
                            {
                                _logger.LogDebug(disposeEx, "Ignoring an error while disposing the stopped Kafka consumer for event {EventType}", eventTypeName);
                            }
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

                            currentConsumer = _consumerFactory(BuildConsumerConfig(subscription));
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

            _logger.LogInformation("Subscribed to event {EventType} on Kafka topic {Topic} as {Mode} group {GroupId}",
                eventTypeName, topic, subscription.IsBroadcast ? "broadcast" : "work", consumerConfig.GroupId);
        }
        catch (Exception ex)
        {
            // 登记发生在起循环之前：起不来就把登记撤掉，否则下一次订阅会被判成「already subscribed」
            _consumers.TryRemove(eventType, out _);
            _consumerCancellationTokens.TryRemove(eventType, out _);
            _logger.LogError(ex, "Failed to subscribe to event {EventType}", eventTypeName);
            throw;
        }
    }

    /// <summary>
    /// 获取事件的所有处理器（支持事件继承）
    /// </summary>
    private static IEnumerable<object> GetEventHandlers(Type eventType, IServiceProvider serviceProvider)
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
        var baseEventHandlers = GetBaseEventHandlers(eventType, serviceProvider);
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
    private static IEnumerable<object> GetBaseEventHandlers(Type eventType, IServiceProvider serviceProvider)
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
    /// 把一条消费记录物化成事件；物化不出来返回 null 并给出原因。
    /// </summary>
    /// <remarks>
    /// 任何从字节到对象这一步抛出的异常都是<b>消息</b>的属性（同样的字节再读多少次结果都一样），
    /// 与连接无关，所以这里不区分异常类型：全部归为毒消息，绝不让它穿到外层的重连处理器。
    /// </remarks>
    private static IEvent? TryDeserialize(ConsumeResult<string, string> result, Type eventType, out string reason)
    {
        if (result.Message.Value == null)
        {
            reason = "tombstone (null value)";
            return null;
        }

        try
        {
            var @event = JsonSerializer.Deserialize(result.Message.Value, eventType, TnziJsonDefaults.Options) as IEvent;
            if (@event == null)
            {
                reason = "payload deserialized to null";
                return null;
            }

            reason = string.Empty;
            return @event;
        }
        catch (Exception ex)
        {
            reason = $"{ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// 毒消息处置：死信启用则投递到 <c>{topic}.dlq</c>（<c>x-dead-letter-reason = deserialization</c>）并提交偏移量；
    /// 死信关闭、或死信投递失败时 <b>Seek 回同一偏移量</b>让分区响亮地卡住。
    /// 广播订阅例外：与广播的处理器失败同一处置 —— 提交、记 Error、<b>丢弃</b>，既不进死信也不 Seek。
    /// </summary>
    /// <remarks>
    /// 「不提交就等于保住了」在 Kafka 里不成立：提交的是分区的位置而不是单条消息，
    /// 该分区下一条消息被提交时这条就一起被跳过了 —— 静默丢弃。Seek 回去、每个轮询周期重读同一条并留下 Critical，
    /// 是 Kafka 语境下唯一诚实的「绝不静默丢弃」：分区停在这里，直到有人把消息移走或打开死信。
    /// Critical 只在卡住的第一个周期与之后每 <see cref="PoisonStallLogEveryCycles"/> 个周期各记一次，其余周期记 Debug。
    /// <para>
    /// 广播两条都不适用：死信主题跨组共享，N 个实例会各投一份同一条毒消息，而重放一条过期的广播本身就是有害的；
    /// Seek 卡住的分区属于按实例的临时组，没有下一个消费者会来接手 —— 卡住的只是本实例自己，
    /// 它从此再也看不到任何后续广播，直到重启。广播处理器本就必须容忍丢失（状态在下一次读取时自愈）。
    /// </para>
    /// </remarks>
    private async Task HandlePoisonMessageAsync(
        IConsumer<string, string> consumer,
        ConsumeResult<string, string> result,
        string topic,
        string eventTypeName,
        string consumerGroupId,
        bool isBroadcast,
        string reason,
        int stallCycles,
        CancellationToken cancellationToken)
    {
        if (isBroadcast)
        {
            consumer.Commit(result);
            _logger.LogError(
                "Broadcast poison message at {TopicPartitionOffset} for event {EventType} could not be deserialized ({Reason}); dropped, offset committed. "
                + "Broadcast messages are never dead-lettered (every instance would file its own copy) and never block the per-instance group.",
                result.TopicPartitionOffset, eventTypeName, reason);
            return;
        }

        if (_options.Consumer.DeadLetterEnabled)
        {
            try
            {
                await ProduceToDeadLetterAsync(topic, result.Message, eventTypeName, eventId: null, consumerGroupId, DeadLetterReasonDeserialization);
                consumer.Commit(result);
                _logger.LogError(
                    "Poison message at {TopicPartitionOffset} for event {EventType} could not be deserialized ({Reason}); routed to the dead-letter topic, offset committed.",
                    result.TopicPartitionOffset, eventTypeName, reason);
                return;
            }
            catch (Exception dlqEx)
            {
                _logger.LogCritical(dlqEx,
                    "Failed to dead-letter the poison message at {TopicPartitionOffset} for event {EventType} ({Reason}); seeking the partition back so it is not skipped.",
                    result.TopicPartitionOffset, eventTypeName, reason);
            }
        }
        else if (stallCycles % PoisonStallLogEveryCycles == 0)
        {
            _logger.LogCritical(
                "Poison message at {TopicPartitionOffset} for event {EventType} could not be deserialized ({Reason}) and dead-letter is disabled. "
                + "The partition is blocked at this offset until the message is removed or Kafka:Consumer:DeadLetterEnabled is turned on (stalled for {Cycles} poll cycle(s)).",
                result.TopicPartitionOffset, eventTypeName, reason, stallCycles + 1);
        }
        else
        {
            _logger.LogDebug("Partition still blocked by the poison message at {TopicPartitionOffset} for event {EventType}", result.TopicPartitionOffset, eventTypeName);
        }

        try
        {
            consumer.Seek(result.TopicPartitionOffset);
        }
        catch (Exception seekEx)
        {
            // 分区在 rebalance 中被收走时 Seek 会失败：那条消息归下一个持有分区的成员处置，本实例无事可做
            _logger.LogWarning(seekEx, "Failed to seek back to {TopicPartitionOffset} for event {EventType}", result.TopicPartitionOffset, eventTypeName);
        }

        if (_options.Consumer.ConsumeErrorBackoffMs > 0)
        {
            await Task.Delay(_options.Consumer.ConsumeErrorBackoffMs, cancellationToken);
        }
    }

    /// <summary>
    /// 跑处理器并把「处理器解析不出来」（DI 构造失败、作用域服务缺失）也计为一次失败，而不是让它穿到外层当成连接故障去重建消费者。
    /// </summary>
    private async Task<int> RunHandlersCountingFailuresAsync(Type eventType, IEvent @event)
    {
        try
        {
            return await RunHandlersAsync(eventType, @event);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resolve or run the handlers for event {EventType} (EventId: {EventId}); counting it as a handler failure.",
                eventType.Name, @event.EventId);
            return 1;
        }
    }

    /// <summary>
    /// 在独立 DI scope 内执行某事件的全部处理器，返回失败的处理器数量。
    /// 单个处理器失败不影响其他处理器（错误隔离），但失败会被计数以决定是否提交偏移量。
    /// </summary>
    private async Task<int> RunHandlersAsync(Type eventType, IEvent @event)
    {
        // 每次（含重试）使用全新 scope，确保 Scoped 处理器被重新解析
        using var handlerScope = _serviceProvider.CreateScope();

        // 按消息携带的 TenantId 切换这个作用域的当前租户，处理器才不会跑在 null 租户作用域里
        // （全局租户过滤器是严格等值：读到空集、写出无主行，且不抛异常）。处理器在 Restore 之后才解析
        using var tenantScope = EventTenantContext.Restore(@event, handlerScope.ServiceProvider);

        var handlers = GetEventHandlers(eventType, handlerScope.ServiceProvider);

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
    private async Task<bool> ExecuteHandlerWithErrorIsolationAsync(object handler, IEvent @event, Type eventType)
    {
        var handlerType = handler.GetType();
        try
        {
            // 获取或创建处理器元数据（编译委托缓存）
            var metadata = EventHandlerInvoker.GetMetadata(handlerType, eventType);

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
                _logger.LogWarning(
                    "Handler {HandlerType} does not have a HandleAsync accepting {EventType} with the expected signature",
                    handlerType.Name, eventType.Name);
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
    private async Task ProduceToDeadLetterAsync(string sourceTopic, Message<string, string> original, string eventTypeName, Guid? eventId, string consumerGroupId, string reason)
    {
        var dlqTopic = $"{sourceTopic}{_options.Consumer.DeadLetterTopicSuffix}";
        var dlqMessage = BuildDeadLetterMessage(original, sourceTopic, eventTypeName, consumerGroupId, reason);

        await _producer.ProduceAsync(dlqTopic, dlqMessage);
        _logger.LogWarning("Event {EventType} (EventId: {EventId}) produced to dead-letter topic {DlqTopic} by consumer group {GroupId} ({Reason}).",
            eventTypeName, eventId?.ToString() ?? "unknown", dlqTopic, consumerGroupId, reason);
    }

    /// <summary>
    /// 组装死信消息：原始键 / 值 / 头原样保留，附加来源主题、事件类型、<b>判死它的消费者组</b>与判死的原因。
    /// </summary>
    /// <remarks>
    /// 死信主题 <c>{topic}.dlq</c> 刻意跨组共享（只记录不重消费）：同一主题的每个组都往同一处写。
    /// 没有组标记，运维分不出是哪个服务失败的，按主题整体重放会把消息重投给<b>每一个</b>组 ——
    /// 包括本来就处理成功的那些。<c>x-dead-letter-consumer-group</c> 是重放工具按组过滤的依据；
    /// <c>x-dead-letter-reason</c> 区分「处理器耗尽重试」（修好处理器后可重放）与「反序列化失败」（重放无益，先修消息）。
    /// </remarks>
    internal static Message<string, string> BuildDeadLetterMessage(
        Message<string, string> original, string sourceTopic, string eventTypeName, string consumerGroupId, string reason = DeadLetterReasonHandlerFailure)
    {
        Check.NotNull(original);
        Check.NotNullOrWhiteSpace(sourceTopic);
        Check.NotNullOrWhiteSpace(eventTypeName);
        Check.NotNullOrWhiteSpace(consumerGroupId);
        Check.NotNullOrWhiteSpace(reason);

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
        headers.Add("x-dead-letter-consumer-group", Encoding.UTF8.GetBytes(consumerGroupId));
        headers.Add("x-dead-letter-reason", Encoding.UTF8.GetBytes(reason));

        return new Message<string, string>
        {
            Key = original.Key,
            Value = original.Value,
            Headers = headers
        };
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
        var baseHandlers = GetBaseEventHandlers(eventType, scope.ServiceProvider);
        foreach (var handler in baseHandlers)
        {
            if (handler != null)
                handlerTypes.Add(handler.GetType());
        }

        return handlerTypes.Count;
    }

    /// <inheritdoc />
    /// <remarks>
    /// ★ 早先这里只打一行 "Runtime subscription is not supported" 就返回 —— 而框架自己
    /// 唯一的分布式订阅点（多实例配置变更广播）走的正是这个方法：调用方拿到成功返回，
    /// 那条链路从未工作过。处理器仍然从 DI 解析（与自动订阅同一条路径）。
    /// </remarks>
    public void Subscribe<TEvent, THandler>()
        where TEvent : class, IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        SubscribeEvent(typeof(TEvent));
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
        SubscribeEvent(typeof(TEvent));
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