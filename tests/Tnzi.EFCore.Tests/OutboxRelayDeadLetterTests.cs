using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.EFCore.Outbox;
using Tnzi.EventBus;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// Outbox 中继的两条可靠性回归：保留期清理必须真的跑起来，
/// 以及重试耗尽的消息必须留下可辨认的死信痕迹而不是被当成「投递成功」抹掉。
/// </summary>
public class OutboxRelayDeadLetterTests
{
    [Fact]
    public async Task Relay_RunsRetentionCleanup_EvenWhenTheBatchIsEmpty()
    {
        // 低流量部署里绝大多数轮询都拿到空批次。清理若挂在批次处理之后，
        // RetentionDays 就是一句永远不会兑现的配置。
        var store = new RecordingEventStore();
        var service = CreateService(store);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.CleanupCalls > 0, "the retention cleanup to run");
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, store.UnprocessedCount);
    }

    [Fact]
    public async Task Relay_DoesNotRunRetentionCleanupOnEveryCycle()
    {
        var store = new RecordingEventStore();
        var service = CreateService(store);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.PollCount >= 3, "three polling cycles");
        await service.StopAsync(CancellationToken.None);

        // 清理是按时间节流的：多轮轮询只应触发一次
        Assert.Equal(1, store.CleanupCalls);
    }

    [Fact]
    public async Task Relay_MarksExhaustedEventAsDeadLetter_NotAsProcessed()
    {
        // 08-29 拆分让大量事件类型换了程序集，此前写进 Outbox 的行 Type.GetType 返回 null。
        // 重试耗尽后若走 MarkAsProcessedAsync，这些行与投递成功的行长得一模一样，
        // 随后被保留期清理连同 LastError 一起删掉 —— 一次静默的数据丢失。
        var store = new RecordingEventStore();
        store.Seed(new StoredEvent
        {
            EventId = Guid.NewGuid(),
            EventType = "Some.Module.ThatMoved.OrderPlacedEvent, Tnzi.Gone",
            EventData = "{}",
            FailureCount = 4
        });

        var service = CreateService(store, maxRetryCount: 5);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.DeadLetteredIds.Count > 0, "the event to be dead-lettered");
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(store.ProcessedIds);
        var error = Assert.Single(store.DeadLetterErrors);
        Assert.Contains("Cannot resolve event type", error);
    }

    [Fact]
    public async Task Relay_KeepsRetrying_BeforeTheRetryBudgetIsSpent()
    {
        var store = new RecordingEventStore();
        store.Seed(new StoredEvent
        {
            EventId = Guid.NewGuid(),
            EventType = "Some.Module.ThatMoved.OrderPlacedEvent, Tnzi.Gone",
            EventData = "{}",
            FailureCount = 0
        });

        var service = CreateService(store, maxRetryCount: 5);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.FailedIds.Count > 0, "the first failure to be recorded");
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(store.DeadLetteredIds);
        Assert.Empty(store.ProcessedIds);
    }

    [Fact]
    public async Task Relay_HandsTheExhaustedEventToTheDeadLetterQueue()
    {
        var store = new RecordingEventStore();
        store.Seed(new StoredEvent
        {
            EventId = Guid.NewGuid(),
            EventType = typeof(RelayTestEvent).AssemblyQualifiedName!,
            EventData = "{}",
            FailureCount = 1
        });

        var deadLetterQueue = new RecordingDeadLetterQueue();
        var service = CreateService(store, maxRetryCount: 2, deadLetterQueue: deadLetterQueue,
            eventBus: new ThrowingEventBus());

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => deadLetterQueue.Added.Count > 0, "the event to reach the dead letter queue");
        await service.StopAsync(CancellationToken.None);

        var added = Assert.Single(deadLetterQueue.Added);
        Assert.IsType<RelayTestEvent>(added);
    }

    #region Helpers

    private static OutboxRelayBackgroundService CreateService(
        IEventStore store,
        int maxRetryCount = 5,
        IEventDeadLetterQueue? deadLetterQueue = null,
        IEventBus? eventBus = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        if (deadLetterQueue is not null) services.AddSingleton(deadLetterQueue);
        if (eventBus is not null) services.AddSingleton(eventBus);

        var provider = services.BuildServiceProvider();

        var options = Microsoft.Extensions.Options.Options.Create(new OutboxOptions
        {
            Enabled = true,
            PollingIntervalSeconds = 1,
            BatchSize = 10,
            MaxRetryCount = maxRetryCount
        });

        return new OutboxRelayBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(), options,
            NullLogger<OutboxRelayBackgroundService>.Instance);
    }

    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for {because}.");
    }

    public sealed class RelayTestEvent : EventBase, IIntegrationEvent
    {
        public string SourceService => "tests";
    }

    private sealed class ThrowingEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
            => throw new InvalidOperationException("broker unavailable");

        public Task PublishDelayedAsync<TEvent>(TEvent @event, TimeSpan delay, CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
            => throw new NotSupportedException();

        public bool HasHandlers<TEvent>() where TEvent : class, IEvent => false;

        public int GetHandlerCount<TEvent>() where TEvent : class, IEvent => 0;

        public void Subscribe<TEvent, THandler>()
            where TEvent : class, IEvent
            where THandler : class, IEventHandler<TEvent>
            => throw new NotSupportedException();

        public void Unsubscribe<TEvent, THandler>()
            where TEvent : class, IEvent
            where THandler : class, IEventHandler<TEvent>
            => throw new NotSupportedException();

        public void UnsubscribeAll<TEvent>() where TEvent : class, IEvent
            => throw new NotSupportedException();
    }

    private sealed class RecordingDeadLetterQueue : IEventDeadLetterQueue
    {
        private readonly List<IEvent> _added = [];

        public IReadOnlyList<IEvent> Added
        {
            get { lock (_added) return _added.ToArray(); }
        }

        public Task AddAsync<TEvent>(TEvent @event, Type handlerType, Exception exception,
            CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
        {
            lock (_added) _added.Add(@event);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DeadLetterEvent>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DeadLetterEvent>>([]);

        public Task RemoveAsync(Guid eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>
    /// 记录中继动作的事件存储：把行留在内存里，使失败计数能真的累加。
    /// </summary>
    private sealed class RecordingEventStore : IEventStore
    {
        private readonly Dictionary<Guid, StoredEvent> _rows = [];
        private readonly List<Guid> _processed = [];
        private readonly List<Guid> _failed = [];
        private readonly List<Guid> _deadLettered = [];
        private readonly List<string> _deadLetterErrors = [];
        private int _pollCount;
        private int _cleanupCalls;

        public int PollCount => Volatile.Read(ref _pollCount);
        public int CleanupCalls => Volatile.Read(ref _cleanupCalls);

        public IReadOnlyList<Guid> ProcessedIds { get { lock (_rows) return _processed.ToArray(); } }
        public IReadOnlyList<Guid> FailedIds { get { lock (_rows) return _failed.ToArray(); } }
        public IReadOnlyList<Guid> DeadLetteredIds { get { lock (_rows) return _deadLettered.ToArray(); } }
        public IReadOnlyList<string> DeadLetterErrors { get { lock (_rows) return _deadLetterErrors.ToArray(); } }

        public int UnprocessedCount
        {
            get { lock (_rows) return _rows.Values.Count(r => !r.IsProcessed); }
        }

        public void Seed(StoredEvent storedEvent)
        {
            lock (_rows) _rows[storedEvent.EventId] = storedEvent;
        }

        public Task<IEnumerable<StoredEvent>> GetUnprocessedEventsAsync(
            int count = 100, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _pollCount);
            lock (_rows)
            {
                return Task.FromResult<IEnumerable<StoredEvent>>(
                    _rows.Values.Where(r => !r.IsProcessed).Take(count).ToList());
            }
        }

        public Task SaveEventAsync(IEvent @event, string eventType, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkAsProcessedAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            lock (_rows)
            {
                _processed.Add(eventId);
                if (_rows.TryGetValue(eventId, out var row))
                {
                    row.IsProcessed = true;
                    row.LastError = null;
                }
            }
            return Task.CompletedTask;
        }

        public Task MarkAsDeadLetterAsync(Guid eventId, string error, CancellationToken cancellationToken = default)
        {
            lock (_rows)
            {
                _deadLettered.Add(eventId);
                _deadLetterErrors.Add(error);
                if (_rows.TryGetValue(eventId, out var row))
                {
                    row.IsProcessed = true;
                    row.LastError = error;
                }
            }
            return Task.CompletedTask;
        }

        public Task MarkAsFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default)
        {
            lock (_rows)
            {
                _failed.Add(eventId);
                if (_rows.TryGetValue(eventId, out var row))
                {
                    row.FailureCount++;
                    row.LastError = error;
                }
            }
            return Task.CompletedTask;
        }

        public Task<StoredEvent?> GetEventAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            lock (_rows)
            {
                return Task.FromResult(_rows.TryGetValue(eventId, out var row) ? row : null);
            }
        }

        public Task<IPagedList<StoredEvent>> GetEventsAsync(EventQueryDto query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The relay never queries the event log.");

        public Task<int> DeleteExpiredEventsAsync(int days = 90, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _cleanupCalls);
            return Task.FromResult(0);
        }
    }

    #endregion
}
