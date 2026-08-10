

namespace Tnzi.RabbitMQ;

/// <summary>
/// RabbitMQ事件总线实现(分布式)
/// 实现 IDistributedEventBus 与 IIntegrationEventBus,PublishAsync 完成仅代表投递成功,
/// 不执行本进程内处理器;不替换 IEventBus(本地总线始终可用)
/// 使用独立的发布 Channel 和 per-consumer Channel 架构（RabbitMQ 最佳实践）
/// RabbitMQ.Client 7.x 的 IChannel 是线程安全的，发布操作无需外部锁
/// </summary>
/// <remarks>
/// 本文件是<b>发布侧与生命周期</b>（Channel 池 / 发布 / 释放）；<b>消费侧</b>（订阅、
/// 处理器发现与执行、确认与重试死信处置）在 <c>RabbitMQEventBus.Consuming.cs</c>。
/// 两件事共用连接与 Channel 策略，但读起来互不相干，合在一个文件里只会让人两头翻。
/// </remarks>
public partial class RabbitMQEventBus : IDistributedEventBus, IIntegrationEventBus, IAsyncDisposable, IDisposable
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

        // 从这里往下的每条失败路径都必须归还许可：只有成功返回的 Channel 才会
        // 经 ReturnPooledChannel 释放许可，否则池许可会被永久吞掉（连续失败后
        // 达到 MaxSize 次即整个发布路径无限期挂在 WaitAsync 上）。
        try
        {
            // Try to get an existing healthy channel from the pool
            while (_channelPool!.TryTake(out var channel))
            {
                if (channel.IsOpen)
                    return channel;

                // Channel is dead, dispose it and fall through to create a fresh one
                try { await channel.DisposeAsync(); }
                catch { /* ignore cleanup errors */ }
            }

            // No available channel, create a new one
            return await CreateAndInitializeChannelAsync(cancellationToken);
        }
        catch
        {
            _channelPoolSemaphore.Release();
            throw;
        }
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
