namespace Tnzi.EFCore.Outbox;

/// <summary>
/// Outbox 消息中继后台服务
/// 定期轮询未处理的 Outbox 消息，反序列化并通过事件总线发布
/// </summary>
/// <remarks>
/// <b>多实例策略：全局互斥锁（单写者）。</b>中继是把库里的消息搬到总线的搬运工，
/// 并行不会更快，只会让同一条消息被每个实例各投递一遍 —— 查询没有认领动作，
/// 两个实例同时轮询拿到的就是同一批。集成事件本就是 at-least-once、消费端必须幂等
/// （见 <c>docs/coding-standards/events.md</c> 事件消费语义），所以重复不破坏正确性；
/// 但重复量会随实例数线性放大，与"broker 偶发重投"不是一个量级。因此每轮轮询先抢
/// <see cref="RelayLockKey"/>，抢不到就跳过本轮。
/// <para>
/// 没有 <see cref="IDistributedLock"/> 实现（未加载 Redis 之类）时退化为无互斥，
/// 单实例部署下完全正确，多实例部署下启动即告警。
/// </para>
/// </remarks>
public class OutboxRelayBackgroundService : BackgroundService
{
    /// <summary>
    /// 中继互斥锁的键。
    /// </summary>
    private const string RelayLockKey = "Tnzi:Outbox:Relay";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxRelayBackgroundService> _logger;

    /// <summary>
    /// 清理计数器，每 N 次轮询执行一次过期消息清理
    /// </summary>
    private int _cleanupCounter;

    private const int CleanupInterval = 100;

    /// <summary>
    /// 缓存反射 MethodInfo，避免每次发布事件时重复查找
    /// </summary>
    private static readonly MethodInfo _integrationPublishMethod =
        typeof(IIntegrationEventBus).GetMethod(nameof(IIntegrationEventBus.PublishAsync))
        ?? throw new InvalidOperationException($"Cannot find PublishAsync method on {nameof(IIntegrationEventBus)}");
    private static readonly MethodInfo _eventBusPublishMethod =
        typeof(IEventBus).GetMethod(nameof(IEventBus.PublishAsync))
        ?? throw new InvalidOperationException($"Cannot find PublishAsync method on {nameof(IEventBus)}");

    public OutboxRelayBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxOptions> options,
        ILogger<OutboxRelayBackgroundService> logger)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Outbox relay is disabled");
            return;
        }

        _logger.LogInformation("Outbox relay started. Polling interval: {Interval}s, batch size: {BatchSize}",
            _options.PollingIntervalSeconds, _options.BatchSize);

        await WarnIfRelayCannotBeSerialisedAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // 正常关闭
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during outbox relay processing");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Outbox relay stopped");
    }

    /// <summary>
    /// 中继无法互斥时告警一次。运维必须知道自己处在哪种模式，而不该靠读源码发现。
    /// </summary>
    private async Task WarnIfRelayCannotBeSerialisedAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IDistributedLock>() is not null) return;

        _logger.LogWarning(
            "Outbox relay is running without an IDistributedLock implementation. This is correct for a "
            + "single instance, but in a multi-instance deployment every instance relays the same batch, "
            + "multiplying delivery duplicates by the instance count. Load a module that provides "
            + "IDistributedLock (e.g. Tnzi.Redis) to serialise the relay across instances.");
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var eventStore = scope.ServiceProvider.GetService<IEventStore>();
        if (eventStore == null)
        {
            _logger.LogDebug("IEventStore not available, skipping outbox relay");
            return;
        }

        var distributedLock = scope.ServiceProvider.GetService<IDistributedLock>();
        if (distributedLock is null)
        {
            await RelayBatchAsync(eventStore, scope.ServiceProvider, stoppingToken);
            return;
        }

        // timeout: null 表示立即返回。抢不到说明另一个实例正在中继本轮 —— 跳过就好，
        // 下一个轮询周期会再来；排队等锁只会让所有实例挤在同一时刻醒来。
        await using var handle = await distributedLock.AcquireAsync(RelayLockKey, timeout: null, stoppingToken);
        if (handle is null || !handle.IsAcquired)
        {
            _logger.LogDebug("Outbox relay skipped this cycle: another instance holds the relay lock");
            return;
        }

        await RelayBatchAsync(eventStore, scope.ServiceProvider, stoppingToken);
    }

    private async Task RelayBatchAsync(IEventStore eventStore, IServiceProvider services,
        CancellationToken stoppingToken)
    {
        var events = await eventStore.GetUnprocessedEventsAsync(_options.BatchSize, stoppingToken);
        var eventList = events.ToList();
        if (eventList.Count == 0) return;

        // 优先使用 IIntegrationEventBus，否则回退到 IEventBus
        var integrationEventBus = services.GetService<IIntegrationEventBus>();
        var eventBus = services.GetService<IEventBus>();

        foreach (var storedEvent in eventList)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await PublishStoredEventAsync(storedEvent, integrationEventBus, eventBus, stoppingToken);
                await eventStore.MarkAsProcessedAsync(storedEvent.EventId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish outbox event {EventId} ({EventType})",
                    storedEvent.EventId, storedEvent.EventType);

                await eventStore.MarkAsFailedAsync(storedEvent.EventId, ex.Message, stoppingToken);

                // 超过最大重试次数，标记为已处理（死信），避免无限重试
                // 使用 MarkAsFailedAsync 后的实际 FailureCount（已在 DB 原子递增）
                var updatedEvent = await eventStore.GetEventAsync(storedEvent.EventId, stoppingToken);
                if (updatedEvent != null && updatedEvent.FailureCount >= _options.MaxRetryCount)
                {
                    _logger.LogError("Outbox event {EventId} ({EventType}) exceeded max retry count ({MaxRetry}), marking as dead letter",
                        storedEvent.EventId, storedEvent.EventType, _options.MaxRetryCount);
                    await eventStore.MarkAsProcessedAsync(storedEvent.EventId, stoppingToken);
                }
            }
        }

        // 定期清理过期消息
        _cleanupCounter++;
        if (_cleanupCounter >= CleanupInterval)
        {
            _cleanupCounter = 0;
            try
            {
                var deleted = await eventStore.DeleteExpiredEventsAsync(_options.RetentionDays, stoppingToken);
                if (deleted > 0)
                    _logger.LogInformation("Cleaned up {Count} expired outbox messages", deleted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up expired outbox messages");
            }
        }
    }

    /// <summary>
    /// 反序列化并发布存储的事件
    /// </summary>
    private async Task PublishStoredEventAsync(
        StoredEvent storedEvent,
        IIntegrationEventBus? integrationEventBus,
        IEventBus? eventBus,
        CancellationToken cancellationToken)
    {
        var eventType = Type.GetType(storedEvent.EventType);
        if (eventType == null)
        {
            throw new InvalidOperationException($"Cannot resolve event type: {storedEvent.EventType}");
        }

        var @event = JsonSerializer.Deserialize(storedEvent.EventData, eventType);
        if (@event == null)
        {
            throw new InvalidOperationException($"Failed to deserialize event data for type: {storedEvent.EventType}");
        }

        // 如果是集成事件且 IIntegrationEventBus 可用，使用集成事件总线发布
        if (integrationEventBus != null && typeof(IIntegrationEvent).IsAssignableFrom(eventType))
        {
            var task = (Task?)_integrationPublishMethod.MakeGenericMethod(eventType).Invoke(integrationEventBus, [@event, cancellationToken])
                ?? throw new InvalidOperationException("PublishAsync returned null");
            await task;
            return;
        }

        // 回退到进程内事件总线
        if (eventBus != null)
        {
            var task = (Task?)_eventBusPublishMethod.MakeGenericMethod(eventType).Invoke(eventBus, [@event, cancellationToken])
                ?? throw new InvalidOperationException("PublishAsync returned null");
            await task;
            return;
        }

        throw new InvalidOperationException("No event bus available to publish outbox event. " +
            "Please register IIntegrationEventBus or IEventBus.");
    }
}
