namespace Tnzi.RabbitMQ;

/// <summary>
/// <see cref="RabbitMQEventBus"/> 的消费侧：订阅、处理器发现与执行，
/// 以及「一条消息处理完之后该怎么向代理表态」。
/// </summary>
/// <remarks>
/// ★ 本文件的核心不变量是<b>确认动作只发生一次</b>：先在 <c>try</c> 内决定
/// <see cref="RabbitMQEventBus.ConsumeOutcome"/>，再在 <c>try</c> 外按结果表态一次。
/// 早先「干活」与「表态」同处一个 <c>try</c>，而 <c>catch</c> 也会去处置错误 ——
/// 一旦 Nack/重发本身抛异常，同一个 DeliveryTag 会被确认两次，
/// RabbitMQ 以 PRECONDITION_FAILED 关掉整条 channel，该事件类型就此静默停止消费。
/// </remarks>
public partial class RabbitMQEventBus
{
    /// <summary>
    /// 订阅事件（每个订阅创建独立的 Channel，RabbitMQ 最佳实践）
    /// </summary>
    public Task SubscribeEventAsync<TEvent>() where TEvent : class, IEvent
        => SubscribeEventAsync(typeof(TEvent));

    /// <summary>
    /// 订阅事件（按运行时类型）。
    /// </summary>
    /// <remarks>
    /// 泛型重载只是这个方法的一层门面。消费侧真正需要的类型信息只有「事件的具体类型」
    /// 一项（反序列化目标、路由键、处理器接口的类型实参），泛型参数在原实现里除了约束
    /// 之外一处也没用到 —— 做成非泛型之后，<see cref="DistributedEventSubscriptionInitializer"/>
    /// 才能按启动时发现的 <see cref="Type"/> 逐个订阅，不必绕反射去拼泛型方法。
    /// </remarks>
    public async Task SubscribeEventAsync(Type eventType, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Check.NotNull(eventType);

        if (!typeof(IEvent).IsAssignableFrom(eventType) || !eventType.IsClass || eventType.IsAbstract)
        {
            throw new ArgumentException(
                $"Type '{eventType.FullName}' cannot be subscribed: it must be a non-abstract class implementing IEvent.",
                nameof(eventType));
        }

        // 队列名由核心的 DistributedConsumerIdentity 决定，与 Kafka 侧共用同一段命名代码：
        // 工作队列 {组}.{事件}（同一服务的实例共用、代理分发），广播 {组}.{事件}.{实例}（每实例一条）。
        // 此前是 Tnzi.Events.{事件}：同一代理上所有 Tnzi 进程竞争消费同一条队列，
        // 每条消息只到达其中一个 —— 没有异常、没有 basic.return、没有任何症状
        var topology = _consumerIdentity.Describe(eventType);
        var eventTypeName = topology.EventName;
        var queueName = topology.ConsumerName;
        var deadLetterQueueName = topology.DeadLetterName;
        var isBroadcast = topology.IsBroadcast;

        // TryAdd 作为并发锁定，防止重复订阅。
        // ★ 这不是异常情况：启动期的自动订阅与应用自己调用 Subscribe 会同时到达同一个类型，
        // 所以只记 Debug —— 用 Warning 会让每个正常启动都刷出一片看起来出事了的日志。
        var subscription = new ConsumerSubscription(queueName);
        if (!_subscriptions.TryAdd(eventType, subscription))
        {
            _logger.LogDebug("Event {EventType} is already subscribed; the request is a no-op", eventTypeName);
            return;
        }

        try
        {
            // 每个消费者创建独立的 Channel（消除 publish/consume 跨 Channel 锁竞争）。
            // ★ 派发并发度必须显式给：客户端默认 1，同一 Channel 上的投递串行派发，
            // 而重试退避的 Task.Delay 就跑在回调里 —— 一条失败消息会按 1s/2s/4s 阻塞该事件类型的全部消费。
            // 缺省跟随 PrefetchCount：预取 N 条却只派发 1 条，其余 N-1 条只是躺在缓冲里。
            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: false,
                publisherConfirmationTrackingEnabled: false,
                consumerDispatchConcurrency: _options.ConsumerDispatchConcurrency ?? _options.PrefetchCount);
            var channel = await _connection.CreateChannelAsync(channelOptions, cancellationToken);
            subscription.Channel = channel;

            // 声明交换机（幂等，确保消费者 Channel 可访问）
            await channel.ExchangeDeclareAsync(_exchangeName, ExchangeType.Topic, true, false);
            await channel.ExchangeDeclareAsync(_options.DeadLetterExchange, ExchangeType.Topic, true, false);

            // prefetch 限制的是未确认消息的数量，不是并发：并发由上面的 ConsumerDispatchConcurrency 决定
            await channel.BasicQosAsync(0, _options.PrefetchCount, false);

            if (isBroadcast)
            {
                // 广播：每个实例一条独占、自动删除、不持久的队列。实例下线队列随连接消失，
                // 重启后的进程是一个新实例、建一条新队列 —— 离线期间的广播不补投，
                // 也不挂死信：一条过期的广播重放出来是有害的（配置早就又改过了）
                await channel.QueueDeclareAsync(queueName, false, true, true, null);
            }
            else
            {
                // 工作队列：持久、非独占，挂死信交换机。死信路由键用本队列名而不是事件名 ——
                // 死信交换机是共享的 topic，按事件名路由会让 A 组的死信同时落进 B 组的死信队列
                await channel.QueueDeclareAsync(deadLetterQueueName, true, false, false, null);
                await channel.QueueBindAsync(deadLetterQueueName, _options.DeadLetterExchange, queueName);

                var queueArgs = new Dictionary<string, object?>
                {
                    { "x-dead-letter-exchange", _options.DeadLetterExchange },
                    { "x-dead-letter-routing-key", queueName }
                };
                await channel.QueueDeclareAsync(queueName, true, false, false, queueArgs);
            }

            await channel.QueueBindAsync(queueName, _exchangeName, eventTypeName);

            // 创建消费者
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, ea) =>
            {
                if (_disposed) return;

                // ★ 「干活」与「向代理表态」分成两段：确认动作只在 try 之外发生一次。
                // 早先两者都在同一个 try 里，而 catch 也会调 HandleConsumerErrorAsync ——
                // 于是一旦 Nack/重发本身抛异常（网络抖动、channel 已关），同一个 DeliveryTag
                // 会被确认两次，RabbitMQ 以 PRECONDITION_FAILED 关掉整条 channel，
                // 这个事件类型就此静默停止消费直到进程重启。
                var outcome = ConsumeOutcome.Acknowledge;

                using (var handlerScope = _serviceProvider.CreateScope())
                {
                    try
                    {
                        var @event = TryDeserialize(ea.Body.Span, eventType, out var poisonReason);

                        if (@event == null)
                        {
                            // 毒消息：反序列化不出来的字节重投多少次都是同样的结果，因此不走重试，
                            // 直接进死信。早先这里是 ACK —— 消息连同它记录的那件事一起消失，
                            // 只在日志里留一行 warning。
                            _logger.LogWarning(
                                "Failed to deserialize event {EventType} ({Reason}); routing the message to the dead letter exchange",
                                eventTypeName, poisonReason);
                            outcome = ConsumeOutcome.DeadLetter;
                        }
                        else
                        {
                            // 按消息携带的 TenantId 切换这个作用域的当前租户，处理器才不会跑在
                            // null 租户作用域里（全局租户过滤器是严格等值：读到空集、写出无主行，且不抛异常）。
                            // 处理器在 Restore 之后才解析：同一作用域里拿到的是同一个 ICurrentTenant 实例
                            using var tenantScope = EventTenantContext.Restore(@event, handlerScope.ServiceProvider);

                            var handlers = GetEventHandlers(eventType, handlerScope.ServiceProvider);
                            var tasks = new List<Task<bool>>();
                            foreach (var handler in handlers)
                            {
                                if (handler != null)
                                    tasks.Add(ExecuteHandlerWithErrorIsolationAsync(handler, @event, eventType));
                            }

                            var failureCount = 0;
                            if (tasks.Count > 0)
                            {
                                var results = await Task.WhenAll(tasks);
                                foreach (var succeeded in results)
                                {
                                    if (!succeeded)
                                        failureCount++;
                                }
                            }

                            // ★ 有处理器失败就绝不 ACK。错误隔离的作用范围是"处理器之间"
                            // （一个失败不拖累其余），不是"对代理隐瞒失败" —— 后者会让消息被确认后
                            // 永久消失：不重试、不进死信、除日志外无迹可寻。
                            if (failureCount > 0)
                            {
                                _logger.LogError(
                                    "{FailureCount} of {HandlerCount} handler(s) failed for event {EventType} (EventId: {EventId}); the message will be retried or dead-lettered",
                                    failureCount, tasks.Count, eventTypeName, @event.EventId);
                                outcome = ConsumeOutcome.Retry;
                            }
                            else
                            {
                                _logger.LogDebug("Processed event {EventType} with ID {EventId}", eventTypeName, @event.EventId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // 基础设施异常（作用域创建、处理器解析、租户恢复）：当作可重试处理，与处理器失败同一条路径。
                        // 反序列化这一步的异常不会到这里 —— TryDeserialize 已把它归为毒消息。
                        _logger.LogError(ex, "Error processing event {EventType}", eventTypeName);
                        outcome = ConsumeOutcome.Retry;
                    }
                }

                try
                {
                    switch (outcome)
                    {
                        case ConsumeOutcome.DeadLetter:
                            // 工作队列声明了 x-dead-letter-exchange，故 requeue: false 即"投进死信"；
                            // 广播队列没有死信，同一动作即丢弃 —— 毒消息本来就重投无益。
                            await channel.BasicNackAsync(ea.DeliveryTag, false, false);
                            break;
                        case ConsumeOutcome.Retry when isBroadcast:
                            await HandleBroadcastConsumerErrorAsync(channel, ea, eventTypeName);
                            break;
                        case ConsumeOutcome.Retry:
                            await HandleConsumerErrorAsync(channel, ea, queueName, eventTypeName);
                            break;
                        default:
                            await channel.BasicAckAsync(ea.DeliveryTag, false);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // 表态本身失败就到此为止，绝不再补一次 —— 重复确认同一个 DeliveryTag 会让
                    // 代理关掉整条 channel。未确认的消息会在 channel 关闭时由代理自动重投。
                    _logger.LogError(ex,
                        "Failed to acknowledge event {EventType} with outcome {Outcome}; the broker will redeliver it when the channel closes",
                        eventTypeName, outcome);
                }
            };

            // 开始消费（在消费者自己的 Channel 上）
            await channel.BasicConsumeAsync(queueName, false, consumer, cancellationToken);

            _logger.LogInformation("Subscribed to event {EventType} on {Mode} queue {QueueName} (dedicated channel)",
                eventTypeName, isBroadcast ? "broadcast" : "work", queueName);
        }
        catch (Exception ex)
        {
            _subscriptions.TryRemove(eventType, out _);
            _logger.LogError(ex, "Failed to subscribe to event {EventType}", eventTypeName);
            throw new RabbitMQException($"Failed to subscribe to event {eventTypeName}", queueName, ex);
        }
    }

    /// <summary>
    /// 一条消息的处理器跑完之后，该怎么向代理表态。
    /// </summary>
    /// <remarks>
    /// 把"决定"与"表态"拆开的意义在于：表态动作只发生一次。三态各自对应一条不可混用的路径 ——
    /// 确认（成功）、直接进死信（毒消息，重投无益）、走重试预算（处理器或基础设施失败）。
    /// </remarks>
    private enum ConsumeOutcome
    {
        /// <summary>全部成功：确认。</summary>
        Acknowledge,

        /// <summary>可重试的失败：交给 <see cref="HandleConsumerErrorAsync"/> 按预算重发或进死信。</summary>
        Retry,

        /// <summary>毒消息：直接进死信，不消耗重试预算。</summary>
        DeadLetter
    }

    /// <summary>
    /// 广播队列上的处理失败：原队列重投一次，再失败即丢弃。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 工作队列那套「重发副本 → 死信」在这里都不适用：副本经交换机会扇出到<b>每一个实例</b>
    /// （其它实例并没有失败），死信里的广播重放出来是有害的。<c>BasicNack(requeue: true)</c>
    /// 只回到本实例自己的队列，是唯一不会波及别人的重试；<c>Redelivered</c> 标志在单消费者的
    /// 独占队列上恰好精确等于「已经重投过一次」，故第二次失败直接 <c>requeue: false</c>（无死信即丢弃），
    /// 不会陷入无限重投。广播事件的处理器本就必须容忍丢失。
    /// </para>
    /// <para>
    /// ★ 重投之前先按 <c>RetryDelay</c> 退避一次（首次重试的那一档）：<c>requeue</c> 是代理立刻重投，
    /// 广播真会撞上的失败（重载配置时数据库抖一下、SignalR hub 还没就绪）在几微秒后照样失败，
    /// 不退避的「重试一次」在实践中就是单发。等待期间这条投递占着一个 prefetch 名额，与工作队列的退避同一代价。
    /// </para>
    /// </remarks>
    private async Task HandleBroadcastConsumerErrorAsync(IChannel consumerChannel, BasicDeliverEventArgs ea, string eventTypeName)
    {
        if (!ea.Redelivered)
        {
            var delay = _options.RetryDelay.GetDelay(1);
            _logger.LogWarning(
                "Broadcast event {EventType} failed; requeueing it once on this instance's own queue after {DelayMs}ms",
                eventTypeName, delay.TotalMilliseconds);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            await consumerChannel.BasicNackAsync(ea.DeliveryTag, false, true);
            return;
        }

        _logger.LogError(
            "Broadcast event {EventType} failed again after one redelivery; dropping it. " +
            "Broadcast handlers must tolerate loss: the state must heal itself on the next read.",
            eventTypeName);
        await consumerChannel.BasicNackAsync(ea.DeliveryTag, false, false);
    }

    /// <summary>
    /// 处理消费者错误：重试或发送到死信队列
    /// Nack 在消费者 Channel 上，republish 在发布 Channel 上（各自独立）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 主队列声明了 <c>x-dead-letter-exchange</c>，因此 <c>BasicNack(requeue: false)</c>
    /// 等价于"投进死信队列"。重试分支绝不能先 Nack：那会让每一次失败都在死信队列
    /// 留下一份副本，与"仅超过 MaxRetryCount 才进死信"的语义相矛盾。重试分支的顺序是
    /// 退避 → 重新发布带递增计数的副本 → ACK 原消息；重发失败才 Nack 走死信，避免静默丢失。
    /// </para>
    /// <para>
    /// ★ 副本发到<b>默认交换机、路由键 = 本队列名</b>，而不是事件交换机 + 事件名：
    /// 队列带上消费者组身份之后，同一个交换机上有多个组各自的队列绑定同一个事件名，
    /// 经交换机重发会让副本扇出到<b>每一个组</b> —— 别的服务因为本服务的一次处理失败多收一份重复事件。
    /// </para>
    /// </remarks>
    private async Task HandleConsumerErrorAsync(IChannel consumerChannel, BasicDeliverEventArgs ea, string queueName, string eventTypeName)
    {
        var retryCount = GetRetryCount(ea.BasicProperties);
        if (retryCount >= _options.MaxRetryCount)
        {
            _logger.LogWarning(
                "Event {EventType} exceeded max retry count ({MaxRetryCount}), sending to dead letter queue",
                eventTypeName, _options.MaxRetryCount);
            // requeue=false，消息会被发送到死信交换机
            await consumerChannel.BasicNackAsync(ea.DeliveryTag, false, false);
            return;
        }

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
            await publishChannel.BasicPublishAsync(string.Empty, queueName, true, newProperties, ea.Body);

            // 副本已入队，原消息才可以确认（顺序反过来会在重发失败时丢消息）
            await consumerChannel.BasicAckAsync(ea.DeliveryTag, false);
        }
        catch (Exception republishEx)
        {
            _logger.LogError(republishEx,
                "Failed to republish event {EventType} for retry; routing the original message to the dead letter exchange",
                eventTypeName);

            try
            {
                await consumerChannel.BasicNackAsync(ea.DeliveryTag, false, false);
            }
            catch (Exception nackEx)
            {
                _logger.LogError(nackEx,
                    "Failed to nack event {EventType} after the retry republish failed; the broker will redeliver it when the channel closes",
                    eventTypeName);
            }
        }
    }

    /// <summary>
    /// 从消息 header 中获取重试次数
    /// </summary>
    /// <summary>
    /// 从字节到对象。得到 null、或这一步抛出的<b>任何</b>异常，都是消息的属性（同样的字节再读多少次结果都一样），
    /// 与代理和连接无关：统一归为毒消息，绝不走重试预算。与 Kafka 侧同一定义。
    /// </summary>
    private static IEvent? TryDeserialize(ReadOnlySpan<byte> body, Type eventType, out string reason)
    {
        try
        {
            var @event = JsonSerializer.Deserialize(body, eventType, TnziJsonDefaults.Options) as IEvent;
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
                    handlers.Add(handler);
            }
        }

        return handlers;
    }

    /// <summary>
    /// 执行处理器并实现错误隔离（单个处理器失败不影响其他处理器）。
    /// 返回 <see langword="true"/> 表示成功，<see langword="false"/> 表示执行抛了异常。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>返回值必须被调用方消费</b>：它是"这条消息能不能 ACK"的唯一依据。此方法早先返回
    /// <c>Task</c> 并把异常吞在这里，于是 <c>Task.WhenAll</c> 永不抛出、消费回调照常 ACK ——
    /// 处理器全部失败的消息被确认后<b>永久消失</b>：不重试、不进死信、除日志外无迹可寻，
    /// 而重试与死信的机制其实一直都在（<see cref="HandleConsumerErrorAsync"/>），只是够不着。
    /// 同一缺陷在 Kafka 侧已于 2026-05-28 修复，本模块此前未跟进。
    /// </para>
    /// <para>
    /// 两类"不算失败"：条件处理器主动跳过（<c>CanHandle</c> 返回 false），以及处理器缺少
    /// 期望签名的 <c>HandleAsync</c> —— 后者是配置型问题，重投多少次都是同样的结果，
    /// 计为失败只会把一条正常消息推进死信队列。口径与 Kafka 侧逐条一致。
    /// </para>
    /// <para>
    /// <b>代价（刻意接受）</b>：消费语义是 at-least-once，同一条消息里 A 成功、B 失败时整条重投，
    /// A 会被再执行一次，因此处理器必须幂等 —— 这与 Kafka 侧的既有约定相同。
    /// 反过来（失败也 ACK）换来的"不重复"是以静默丢消息为代价的，不是一个可选项。
    /// </para>
    /// </remarks>
    private async Task<bool> ExecuteHandlerWithErrorIsolationAsync(object handler, IEvent @event, Type eventType)
    {
        var handlerType = handler.GetType();
        try
        {
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

            if (metadata.HandleDelegate != null)
            {
                await metadata.HandleDelegate(handler, @event, CancellationToken.None);
            }
            else
            {
                // 配置型问题（缺失 HandleAsync）：重投无益，记录告警但不计为可重试失败
                _logger.LogWarning(
                    "Handler {HandlerType} does not have a HandleAsync accepting {EventType} with the expected signature",
                    handlerType.Name, eventType.Name);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in handler {HandlerType} for event {EventType} (EventId: {EventId}). " +
                "This error is isolated from the other handlers, but the message will not be acknowledged.",
                handlerType.Name, eventType.Name, @event.EventId);
            return false;
        }
    }
}
