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
    /// 下一次执行过期消息清理的时刻。
    /// </summary>
    /// <remarks>
    /// ★ 刻意按**时间**触发而不是按轮询次数。原实现是「每 N 次轮询清理一次」，而计数器
    /// 递增写在「本批为空就提前返回」之后 —— 低流量部署里绝大多数轮询都拿到空批次，
    /// 计数器几乎不动，<c>RetentionDays</c> 于是成了一句永远不会兑现的配置。
    /// 初值是 <see cref="DateTimeOffset.MinValue"/>：进程起来后的第一轮就清一次。
    /// </remarks>
    private DateTimeOffset _nextCleanupAt = DateTimeOffset.MinValue;

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// 缓存反射 MethodInfo，避免每次发布事件时重复查找
    /// </summary>
    private static readonly MethodInfo _integrationPublishMethod =
        typeof(IIntegrationEventBus).GetMethod(nameof(IIntegrationEventBus.PublishAsync))
        ?? throw new InvalidOperationException($"Cannot find PublishAsync method on {nameof(IIntegrationEventBus)}");
    private static readonly MethodInfo _eventBusPublishMethod =
        typeof(IEventBus).GetMethod(nameof(IEventBus.PublishAsync))
        ?? throw new InvalidOperationException($"Cannot find PublishAsync method on {nameof(IEventBus)}");
    private static readonly MethodInfo _deadLetterAddMethod =
        typeof(IEventDeadLetterQueue).GetMethod(nameof(IEventDeadLetterQueue.AddAsync))
        ?? throw new InvalidOperationException($"Cannot find AddAsync method on {nameof(IEventDeadLetterQueue)}");

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
        if (handle is null)
        {
            _logger.LogDebug("Outbox relay skipped this cycle: another instance holds the relay lock");
            return;
        }

        // ★ 锁在批次中途丢失（续租失败、Redis 抖动、键被逐出）= 另一个实例随时会抢到锁并读到
        // 同一批未标记的事件。此时唯一正确的动作是停手：把这一批跑完只会让每条事件投递两次，
        // 而那正是这把锁存在的唯一理由。只在获取瞬间读一次 IsAcquired 发现不了这件事 ——
        // 那一刻它恒为 true。
        using var relayCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, handle.Lost);
        try
        {
            await RelayBatchAsync(eventStore, scope.ServiceProvider, relayCancellation.Token);
        }
        catch (OperationCanceledException) when (handle.Lost.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Outbox relay stopped mid-batch because the relay lock was lost (renewal failed or the key expired). "
                + "Events not yet marked as processed will be relayed again; integration event consumers must be idempotent.");
        }
    }

    private async Task RelayBatchAsync(IEventStore eventStore, IServiceProvider services,
        CancellationToken stoppingToken)
    {
        var events = await eventStore.GetUnprocessedEventsAsync(_options.BatchSize, stoppingToken);
        var eventList = events.ToList();

        if (eventList.Count > 0)
        {
            // 优先使用 IIntegrationEventBus，否则回退到 IEventBus
            var integrationEventBus = services.GetService<IIntegrationEventBus>();
            var eventBus = services.GetService<IEventBus>();

            foreach (var storedEvent in eventList)
            {
                if (stoppingToken.IsCancellationRequested) break;

                await RelayOneAsync(eventStore, services, storedEvent, integrationEventBus, eventBus, stoppingToken);
            }
        }

        // 清理与本批是否为空无关：空批次正是低流量部署的常态
        await CleanUpExpiredIfDueAsync(eventStore, services, stoppingToken);
    }

    private async Task RelayOneAsync(
        IEventStore eventStore,
        IServiceProvider services,
        StoredEvent storedEvent,
        IIntegrationEventBus? integrationEventBus,
        IEventBus? eventBus,
        CancellationToken stoppingToken)
    {
        // 在 try 之外持有：死信路由需要知道事件到底有没有被成功物化 ——
        // 「反序列化不出来」与「投递失败」的处置不同
        MaterializedEvent? materialized = null;

        try
        {
            materialized = MaterializeEvent(storedEvent);
            await PublishStoredEventAsync(materialized.Value, integrationEventBus, eventBus, stoppingToken);
            await eventStore.MarkAsProcessedAsync(storedEvent.EventId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 中继被叫停（进程关闭或锁丢失）不是这条事件的失败：不计失败次数、不推向死信，原样留给下一轮
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish outbox event {EventId} ({EventType})",
                storedEvent.EventId, storedEvent.EventType);

            await eventStore.MarkAsFailedAsync(storedEvent.EventId, ex.Message, stoppingToken);

            // 使用 MarkAsFailedAsync 后的实际 FailureCount（已在 DB 原子递增）
            var updatedEvent = await eventStore.GetEventAsync(storedEvent.EventId, stoppingToken);
            if (updatedEvent == null || updatedEvent.FailureCount < _options.MaxRetryCount)
            {
                return;
            }

            _logger.LogError("Outbox event {EventId} ({EventType}) exceeded max retry count ({MaxRetry}), marking as dead letter",
                storedEvent.EventId, storedEvent.EventType, _options.MaxRetryCount);

            // ★ 不能走 MarkAsProcessedAsync：那与「投递成功」写同一组字段，这些从未送达的
            // 消息会与成功记录混在一起，随后被保留期清理连同 LastError 一起删掉 —— 静默丢失。
            await eventStore.MarkAsDeadLetterAsync(storedEvent.EventId, ex.Message, stoppingToken);

            await RouteToDeadLetterQueueAsync(services, storedEvent, materialized, ex,
                updatedEvent.FailureCount, stoppingToken);
        }
    }

    /// <summary>
    /// 把死信同时交给 <see cref="IEventDeadLetterQueue"/>（若已注册）。
    /// </summary>
    /// <remarks>
    /// Outbox 表本身才是死信的持久记录；这里只是把它送到运维惯常查看的那个面上。
    /// 事件物化不出来（程序集搬家后 <c>Type.GetType</c> 返回 null）时无法构造
    /// <c>AddAsync&lt;TEvent&gt;</c> 的实参，此时只记录 —— 行还在，证据不丢。
    /// </remarks>
    private async Task RouteToDeadLetterQueueAsync(
        IServiceProvider services,
        StoredEvent storedEvent,
        MaterializedEvent? materialized,
        Exception failure,
        int failureCount,
        CancellationToken cancellationToken)
    {
        var deadLetterQueue = services.GetService<IEventDeadLetterQueue>();
        if (deadLetterQueue == null) return;

        if (materialized is null || !typeof(IEvent).IsAssignableFrom(materialized.Value.Type))
        {
            _logger.LogWarning(
                "Outbox event {EventId} ({EventType}) could not be materialised, so it cannot be handed to the "
                + "dead letter queue. The outbox row is the record of it; it is kept out of retention cleanup.",
                storedEvent.EventId, storedEvent.EventType);
            return;
        }

        try
        {
            var task = (Task?)_deadLetterAddMethod
                .MakeGenericMethod(materialized.Value.Type)
                .Invoke(deadLetterQueue,
                    [materialized.Value.Instance, typeof(OutboxRelayBackgroundService), failure, cancellationToken])
                ?? throw new InvalidOperationException("AddAsync returned null");
            await task;
        }
        catch (Exception ex)
        {
            // 死信队列只是第二块展示面，写不进去不该影响本轮其余消息
            _logger.LogError(ex, "Failed to add outbox event {EventId} to the dead letter queue after {FailureCount} attempts",
                storedEvent.EventId, failureCount);
        }
    }

    private async Task CleanUpExpiredIfDueAsync(IEventStore eventStore, IServiceProvider services,
        CancellationToken stoppingToken)
    {
        var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var now = timeProvider.GetUtcNow();
        if (now < _nextCleanupAt) return;

        _nextCleanupAt = now + CleanupInterval;

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

    /// <summary>
    /// 已从存储行物化出来的事件（类型 + 实例）。
    /// </summary>
    private readonly record struct MaterializedEvent(Type Type, object Instance);

    /// <summary>
    /// 解析类型并反序列化存储的事件
    /// </summary>
    private static MaterializedEvent MaterializeEvent(StoredEvent storedEvent)
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

        return new MaterializedEvent(eventType, @event);
    }

    /// <summary>
    /// 发布已物化的事件
    /// </summary>
    private static async Task PublishStoredEventAsync(
        MaterializedEvent materialized,
        IIntegrationEventBus? integrationEventBus,
        IEventBus? eventBus,
        CancellationToken cancellationToken)
    {
        var eventType = materialized.Type;
        var @event = materialized.Instance;

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
