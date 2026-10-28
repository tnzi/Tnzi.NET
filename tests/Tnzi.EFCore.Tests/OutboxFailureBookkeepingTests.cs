using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.EFCore.Outbox;
using Tnzi.EventBus;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 失败记账本身出错时，中继不能卡死在那一条事件上。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <c>LastError</c> 列上限 2000 字符，此前原样写入 <c>ex.Message</c>。处理器或代理客户端抛出一条更长的消息时，
/// SQL Server / PostgreSQL / MySQL 严格模式下这次 UPDATE 本身失败；异常逃出单条事件的处理，中止整批，
/// 失败次数一次都没加上。这条事件仍是最早的未处理行，之后每一轮都卡在它上面：后面的事件永远不再中继，
/// 保留期清理也永远轮不到，只剩一条反复出现的错误日志。
/// </para>
/// <para>
/// 两处修复各守一半：存储把错误截到列长；中继把记账失败当作这一条的问题，记日志后继续处理同批其余事件。
/// SQLite 不校验列长，截断用落库后的长度断言。
/// </para>
/// </remarks>
public class OutboxFailureBookkeepingTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly OutboxTestDbContext _dbContext;
    private readonly SqliteConnection _connection;
    private readonly EfCoreEventStore _store;

    public OutboxFailureBookkeepingTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<ICurrentTenant>(new MockCurrentTenant());

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        services.AddDbContext<OutboxTestDbContext>((_, options) => options.UseSqlite(_connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<OutboxTestDbContext>());

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<OutboxTestDbContext>();
        _dbContext.Database.EnsureCreated();

        _store = new EfCoreEventStore(_serviceProvider, NullLogger<EfCoreEventStore>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task MarkAsFailed_TruncatesTheErrorToTheColumnLength()
    {
        var id = await SeedAsync();

        await _store.MarkAsFailedAsync(id, new string('x', 5000));

        var stored = await _store.GetEventAsync(id);
        Assert.NotNull(stored);
        Assert.Equal(1, stored.FailureCount);
        Assert.Equal(OutboxMessage.LastErrorMaxLength, stored.LastError!.Length);
    }

    [Fact]
    public async Task MarkAsDeadLetter_TruncatesTheErrorToTheColumnLength()
    {
        var id = await SeedAsync();

        await _store.MarkAsDeadLetterAsync(id, new string('y', 5000));

        var stored = await _store.GetEventAsync(id);
        Assert.NotNull(stored);
        Assert.True(stored.IsDeadLetter);
        Assert.Equal(OutboxMessage.LastErrorMaxLength, stored.LastError!.Length);
    }

    [Fact]
    public async Task MarkAsFailed_DoesNotCutASurrogatePairInHalf()
    {
        // 一个增补字符（UTF-16 代理对）恰好跨在列长边界上：按 UTF-16 下标硬切会留下孤立的高代理项，
        // 严格的 UTF-8 编码器（Npgsql）拒绝写入，记账又一次失败；宽松的编码器把它写成替换字符
        var id = await SeedAsync();
        var error = new string('x', OutboxMessage.LastErrorMaxLength - 1) + "\U0001F600" + new string('x', 100);

        await _store.MarkAsFailedAsync(id, error);

        var stored = await _store.GetEventAsync(id);
        Assert.NotNull(stored);
        Assert.Equal(new string('x', OutboxMessage.LastErrorMaxLength - 1), stored.LastError);
    }

    [Fact]
    public async Task Relay_KeepsGoing_WhenRecordingAFailureThrows()
    {
        var poison = new StoredEvent
        {
            EventId = Guid.NewGuid(),
            EventType = "Some.Module.ThatMoved.OrderPlacedEvent, Tnzi.Gone",
            EventData = "{}"
        };
        var healthy = new StoredEvent
        {
            EventId = Guid.NewGuid(),
            EventType = typeof(BookkeepingTestEvent).AssemblyQualifiedName!,
            EventData = "{}"
        };
        var store = new BrokenBookkeepingStore([poison, healthy], failingId: poison.EventId);

        var services = new ServiceCollection();
        services.AddSingleton<IEventStore>(store);
        services.AddSingleton<IEventBus>(new AcceptingEventBus());
        using var provider = services.BuildServiceProvider();
        var service = new OutboxRelayBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new OutboxOptions { Enabled = true, PollingIntervalSeconds = 1, BatchSize = 10 }),
            NullLogger<OutboxRelayBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await WaitForAsync(() => store.ProcessedIds.Contains(healthy.EventId) && store.CleanupCalls > 0);
        await service.StopAsync(CancellationToken.None);

        Assert.Contains(healthy.EventId, store.ProcessedIds);
        Assert.DoesNotContain(poison.EventId, store.ProcessedIds);
    }

    private async Task<Guid> SeedAsync()
    {
        var @event = new BookkeepingTestEvent();
        await _store.SaveEventAsync(@event, typeof(BookkeepingTestEvent).FullName!);
        _dbContext.ChangeTracker.Clear();

        var stored = await _dbContext.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.EventTime == @event.EventTime);
        return stored.Id;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(20);
        }

        throw new TimeoutException("Timed out waiting for the relay.");
    }

    public sealed class BookkeepingTestEvent : EventBase, IIntegrationEvent
    {
        public string SourceService => "tests";
    }

    /// <summary>对指定事件记失败时抛异常（模拟写 LastError 撞上列长），其余记账照常。</summary>
    private sealed class BrokenBookkeepingStore(IReadOnlyList<StoredEvent> rows, Guid failingId) : IEventStore
    {
        private readonly List<Guid> _processed = [];
        private int _cleanupCalls;

        public IReadOnlyList<Guid> ProcessedIds { get { lock (_processed) return _processed.ToArray(); } }

        public int CleanupCalls => Volatile.Read(ref _cleanupCalls);

        public Task<IEnumerable<StoredEvent>> GetUnprocessedEventsAsync(int count = 100, CancellationToken cancellationToken = default)
        {
            lock (_processed)
            {
                return Task.FromResult<IEnumerable<StoredEvent>>(rows.Where(r => !_processed.Contains(r.EventId)).Take(count).ToList());
            }
        }

        public Task MarkAsFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default)
            => eventId == failingId
                ? throw new InvalidOperationException("value too long for type character varying(2000)")
                : Task.CompletedTask;

        public Task MarkAsProcessedAsync(Guid eventId, CancellationToken cancellationToken = default)
        {
            lock (_processed) _processed.Add(eventId);
            return Task.CompletedTask;
        }

        public Task MarkAsDeadLetterAsync(Guid eventId, string error, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<StoredEvent?> GetEventAsync(Guid eventId, CancellationToken cancellationToken = default)
            => Task.FromResult(rows.FirstOrDefault(r => r.EventId == eventId));

        public Task SaveEventAsync(IEvent @event, string eventType, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IPagedList<StoredEvent>> GetEventsAsync(EventQueryDto query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> DeleteExpiredEventsAsync(int days = 90, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _cleanupCalls);
            return Task.FromResult(0);
        }
    }

    private sealed class AcceptingEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
            => Task.CompletedTask;

        public Task PublishDelayedAsync<TEvent>(TEvent @event, TimeSpan delay, CancellationToken cancellationToken = default)
            where TEvent : class, IEvent
            => Task.CompletedTask;

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
}
