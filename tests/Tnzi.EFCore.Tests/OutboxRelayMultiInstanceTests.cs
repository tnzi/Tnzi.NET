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

    #region Helpers

    private static OutboxRelayBackgroundService CreateService(
        IEventStore store, IDistributedLock? distributedLock, out RecordingLogger logger)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        if (distributedLock is not null)
            services.AddSingleton(distributedLock);

        var provider = services.BuildServiceProvider();
        logger = new RecordingLogger();

        var options = Microsoft.Extensions.Options.Options.Create(new OutboxOptions
        {
            Enabled = true,
            PollingIntervalSeconds = 1,
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
        private int _unprocessedQueries;

        public int UnprocessedQueries => Volatile.Read(ref _unprocessedQueries);

        public Task<IEnumerable<StoredEvent>> GetUnprocessedEventsAsync(
            int count = 100, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _unprocessedQueries);
            return Task.FromResult(Enumerable.Empty<StoredEvent>());
        }

        public Task SaveEventAsync(IEvent @event, string eventType, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkAsProcessedAsync(Guid eventId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkAsFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<StoredEvent?> GetEventAsync(Guid eventId, CancellationToken cancellationToken = default)
            => Task.FromResult<StoredEvent?>(null);

        public Task<IPagedList<StoredEvent>> GetEventsAsync(EventQueryDto query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The relay never queries the event log.");

        public Task<int> DeleteExpiredEventsAsync(int days = 90, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class FakeDistributedLock(bool grantsLock) : IDistributedLock
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
                ? new FakeLockHandle(key, () => Interlocked.Increment(ref _releaseCount))
                : null;

            return Task.FromResult(handle);
        }

        public Task<(bool Success, IDistributedLockHandle? Handle)> TryAcquireAsync(
            string key, TimeSpan timeout, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The relay uses AcquireAsync with a null timeout.");
    }

    private sealed class FakeLockHandle(string key, Action onRelease) : IDistributedLockHandle
    {
        public string Key { get; } = key;
        public bool IsAcquired => true;
        public Task<bool> ExtendAsync(TimeSpan extension) => Task.FromResult(true);

        public ValueTask DisposeAsync()
        {
            onRelease();
            return ValueTask.CompletedTask;
        }
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
