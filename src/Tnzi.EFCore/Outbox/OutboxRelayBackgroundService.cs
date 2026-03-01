namespace Tnzi.EFCore.Outbox;

/// <summary>
/// Outbox 消息中继后台服务
/// 定期轮询未处理的 Outbox 消息，反序列化并通过事件总线发布
/// </summary>
public class OutboxRelayBackgroundService : BackgroundService
{
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

    private async Task ProcessOutboxMessagesAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var eventStore = scope.ServiceProvider.GetService<IEventStore>();
        if (eventStore == null)
        {
            _logger.LogDebug("IEventStore not available, skipping outbox relay");
            return;
        }

        var events = await eventStore.GetUnprocessedEventsAsync(_options.BatchSize, stoppingToken);
        var eventList = events.ToList();
        if (eventList.Count == 0) return;

        // 优先使用 IIntegrationEventBus，否则回退到 IEventBus
        var integrationEventBus = scope.ServiceProvider.GetService<IIntegrationEventBus>();
        var eventBus = scope.ServiceProvider.GetService<IEventBus>();

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
                if (storedEvent.FailureCount + 1 >= _options.MaxRetryCount)
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
