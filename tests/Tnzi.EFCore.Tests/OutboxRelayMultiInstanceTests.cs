using Tnzi.EFCore.Outbox;
using Tnzi.EventBus;
using Tnzi.Locking;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// The Outbox relay is a single-writer task: two instances polling at the same time read the
/// same unclaimed batch and each deliver all of it. These tests pin the mutual exclusion that
/// keeps duplicate delivery from scaling with the instance count.
/// </summary>
public class OutboxRelayMultiInstanceTests
{
    [Fact]
    public async Task Relay_SkipsCycle_WhenAnotherInstanceHoldsTheLock()
    {
        var store = new FakeEventStore();
        var busyLock = new FakeDistributedLock(grantsLock: false);
        var service = CreateService(store, busyLock, out _);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => busyLock.AcquireAttempts >= 2, "two relay cycles to attempt the lock");
        await service.StopAsync(CancellationToken.None);

        // Not merely "published nothing" - it must not even read the batch, or the next
        // instance's claim would race against a read that already happened.
        Assert.Equal(0, store.UnprocessedQueries);
    }

    [Fact]
    public async Task Relay_PollsTheStore_WhenItWinsTheLock()
    {
        var store = new FakeEventStore();
        var freeLock = new FakeDistributedLock(grantsLock: true);
        var service = CreateService(store, freeLock, out _);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.UnprocessedQueries > 0, "the relay to poll the store");
        await service.StopAsync(CancellationToken.None);

        Assert.True(freeLock.AcquireAttempts > 0);
        // Releasing matters as much as acquiring: a handle never disposed would wedge every
        // other instance until the lock expired.
        await WaitForAsync(() => freeLock.ReleaseCount > 0, "the relay to release the lock");
    }

    [Fact]
    public async Task Relay_StillRuns_WhenNoDistributedLockIsRegistered()
    {
        var store = new FakeEventStore();
        var service = CreateService(store, distributedLock: null, out _);

        // A missing lock implementation must degrade to the old single-instance behaviour,
        // never to "the relay stops delivering".
        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.UnprocessedQueries > 0, "the relay to poll the store");
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Relay_WarnsAtStartup_WhenNoDistributedLockIsRegistered()
    {
        var store = new FakeEventStore();
        var service = CreateService(store, distributedLock: null, out var logger);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => logger.Warnings.Count > 0, "the startup warning");
        await service.StopAsync(CancellationToken.None);

        // Operators have to learn which mode they are in from the log, not from the source.
        Assert.Contains(logger.Warnings, w => w.Contains("IDistributedLock"));
    }

    [Fact]
    public async Task Relay_DoesNotWarn_WhenADistributedLockIsRegistered()
    {
        var store = new FakeEventStore();
        var freeLock = new FakeDistributedLock(grantsLock: true);
        var service = CreateService(store, freeLock, out var logger);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.UnprocessedQueries > 0, "the relay to poll the store");
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(logger.Warnings);
    }

    /// <summary>
    /// 锁在批次中途丢失（续租失败、Redis 抖动、键被逐出）时中继必须停手：另一个实例随时会抢到锁并读到
    /// 同一批未标记的事件，把这一批跑完只会让每条事件投递两次 —— 正是这把锁存在的唯一理由。
    /// 此前句柄的丢失信号没有任何读者：<c>IsAcquired</c> 只在获取瞬间被读一次，那时恒为 true。
    /// </summary>
    [Fact]
    public async Task Relay_StopsMidBatch_WhenLockIsLost()
    {
        var store = new FakeEventStore();
        for (var i = 0; i < 3; i++)
        {
            store.Seed(new StoredEvent
            {
                EventId = Guid.NewGuid(),
                EventType = typeof(OutboxRelayDeadLetterTests.RelayTestEvent).AssemblyQualifiedName!,
                EventData = "{}"
            });
        }

        var lostSource = new CancellationTokenSource();
        var lockThatGetsLost = new FakeDistributedLock(grantsLock: true, lost: lostSource.Token);
        // 第一条发布成功的瞬间锁丢失：之后的两条不得再中继
        var bus = new LossTriggeringEventBus(onFirstPublish: lostSource.Cancel);
        // 轮询间隔拉长，使「第一轮结束后」的断言只看到一轮：第二轮会照常把剩下两条中继掉
        var service = CreateService(store, lockThatGetsLost, out _, bus, pollingIntervalSeconds: 30);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => lockThatGetsLost.ReleaseCount >= 1, "the first relay cycle to finish");
        await service.StopAsync(CancellationToken.None);

        Assert.Single(store.ProcessedIds);
        Assert.Equal(1, bus.PublishCount);
        // 停手是因为锁丢了，不是因为投递失败：不得把没跑到的事件记成失败
        Assert.Empty(store.FailedIds);
    }

    #region Helpers

    private static OutboxRelayBackgroundService CreateService(
        IEventStore store, IDistributedLock? distributedLock, out RecordingLogger logger, IEventBus? eventBus = null,
        int pollingIntervalSeconds = 1)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        if (distributedLock is not null)
            services.AddSingleton(distributedLock);
        if (eventBus is not null)
            services.AddSingleton(eventBus);

        var provider = services.BuildServiceProvider();
        logger = new RecordingLogger();

        var options = Microsoft.Extensions.Options.Options.Create(new OutboxOptions
        {
            Enabled = true,
            PollingIntervalSeconds = pollingIntervalSeconds,
            BatchSize = 10
        });

        return new OutboxRelayBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(), options, logger);
    }

    private static async Task WaitForAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(20);
        }

        throw new TimeoutException($"Timed out waiting for {because}.");
    }

    private sealed class FakeEventStore : IEventStore
    {
        private readonly Dictionary<Guid, StoredEvent> _rows = [];
        private readonly List<Guid> _processed = [];
        private readonly List<Guid> _failed = [];
        private int _unprocessedQueries;

        public int UnprocessedQueries => Volatile.Read(ref _unprocessedQueries);
        public IReadOnlyList<Guid> ProcessedIds { get { lock (_rows) return _processed.ToArray(); } }
        public IReadOnlyList<Guid> FailedIds { get { lock (_rows) return _failed.ToArray(); } }

        public void Seed(StoredEvent storedEvent)
        {
            lock (_rows) _rows[storedEvent.EventId] = storedEvent;
        }

        public Task<IEnumerable<StoredEvent>> GetUnprocessedEventsAsync(
            int count = 100, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _unprocessedQueries);
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
                if (_rows.TryGetValue(eventId, out var row)) row.IsProcessed = true;
                _processed.Add(eventId);
            }
            return Task.CompletedTask;
        }

        public Task MarkAsFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default)
        {
            lock (_rows) _failed.Add(eventId);
            return Task.CompletedTask;
        }

        public Task<StoredEvent?> GetEventAsync(Guid eventId, CancellationToken cancellationToken = default)
            => Task.FromResult<StoredEvent?>(null);

        public Task<IPagedList<StoredEvent>> GetEventsAsync(EventQueryDto query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The relay never queries the event log.");

        public Task<int> DeleteExpiredEventsAsync(int days = 90, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class FakeDistributedLock(bool grantsLock, CancellationToken lost = default) : IDistributedLock
    {
        private int _acquireAttempts;
        private int _releaseCount;

        public int AcquireAttempts => Volatile.Read(ref _acquireAttempts);
        public int ReleaseCount => Volatile.Read(ref _releaseCount);

        public Task<IDistributedLockHandle?> AcquireAsync(
            string key, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _acquireAttempts);
            IDistributedLockHandle? handle = grantsLock
                ? new FakeLockHandle(key, () => Interlocked.Increment(ref _releaseCount), lost)
                : null;

            return Task.FromResult(handle);
        }

        public Task<(bool Success, IDistributedLockHandle? Handle)> TryAcquireAsync(
            string key, TimeSpan timeout, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The relay uses AcquireAsync with a null timeout.");
    }

    private sealed class FakeLockHandle(string key, Action onRelease, CancellationToken lost) : IDistributedLockHandle
    {
        public string Key { get; } = key;
        public bool IsAcquired => !lost.IsCancellationRequested;
        public CancellationToken Lost => lost;
        public Task<bool> ExtendAsync(TimeSpan extension) => Task.FromResult(true);

        public ValueTask DisposeAsync()
        {
            onRelease();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// 第一次发布时触发一个动作（用例里是「让锁丢失」），之后只计数。
    /// </summary>
    private sealed class LossTriggeringEventBus(Action onFirstPublish) : IEventBus
    {
        private int _publishCount;

        public int PublishCount => Volatile.Read(ref _publishCount);

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
        {
            if (Interlocked.Increment(ref _publishCount) == 1) onFirstPublish();
            return Task.CompletedTask;
        }

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

    private sealed class RecordingLogger : ILogger<OutboxRelayBackgroundService>
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get { lock (_warnings) return _warnings.ToArray(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Warning) return;
            lock (_warnings) _warnings.Add(formatter(state, exception));
        }
    }

    #endregion
}
