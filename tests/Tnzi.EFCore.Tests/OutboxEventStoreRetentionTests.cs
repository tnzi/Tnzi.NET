using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.EFCore.Outbox;
using Tnzi.EventBus;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 承载 <c>OutboxMessage</c> 的最小 DbContext（实体经 IEntityRegister 自动发现）。
/// </summary>
public class OutboxTestDbContext : TnziDbContext<OutboxTestDbContext>
{
    public OutboxTestDbContext(DbContextOptions<OutboxTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // OutboxMessageConfiguration 只在 EFCoreModule 打开 Outbox 时才把实体注册进模型，
        // 而这里没有模块图，故显式映射一份最小形状
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("OutboxMessage");
            b.HasKey(x => x.Id);
            b.Property(x => x.EventType).IsRequired().HasMaxLength(500);
            b.Property(x => x.EventData).IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>
/// Outbox 保留期清理与死信记录的分辨力回归测试。
///
/// 死信（重试耗尽、从未送达）与投递成功的记录必须能区分，否则保留期清理会把唯一的
/// 失败证据连同 LastError 一起删掉，而外观与「都投递成功了」完全一致。
/// 判据刻意不新增列：新增列意味着每个消费应用都要迁移一次。
/// </summary>
public class OutboxEventStoreRetentionTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly OutboxTestDbContext _dbContext;
    private readonly SqliteConnection _connection;
    private readonly EfCoreEventStore _store;

    public OutboxEventStoreRetentionTests()
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

    private async Task<Guid> SeedAsync()
    {
        var @event = new RetentionTestEvent();
        await _store.SaveEventAsync(@event, typeof(RetentionTestEvent).FullName!);
        _dbContext.ChangeTracker.Clear();

        var stored = await _dbContext.Set<OutboxMessage>().AsNoTracking().SingleAsync(m => m.EventTime == @event.EventTime);
        return stored.Id;
    }

    [Fact]
    public async Task MarkAsProcessed_ClearsTheLastError()
    {
        var id = await SeedAsync();

        await _store.MarkAsFailedAsync(id, "transient broker hiccup");
        await _store.MarkAsProcessedAsync(id);

        var reloaded = await _store.GetEventAsync(id);
        Assert.NotNull(reloaded);
        // 残留的错误信息会让一条最终投递成功的记录被当成死信
        Assert.Null(reloaded!.LastError);
        Assert.False(reloaded.IsDeadLetter);
    }

    [Fact]
    public async Task MarkAsDeadLetter_KeepsTheErrorAndIsDistinguishable()
    {
        var id = await SeedAsync();

        await _store.MarkAsDeadLetterAsync(id, "Cannot resolve event type: Gone.OrderPlaced, Tnzi.Gone");

        var reloaded = await _store.GetEventAsync(id);
        Assert.NotNull(reloaded);
        Assert.True(reloaded!.IsProcessed);
        Assert.True(reloaded.IsDeadLetter);
        Assert.Contains("Cannot resolve event type", reloaded.LastError);
    }

    [Fact]
    public async Task DeleteExpiredEvents_KeepsDeadLettersAndDeletesDeliveredOnes()
    {
        var delivered = await SeedAsync();
        await _store.MarkAsProcessedAsync(delivered);

        var deadLetter = await SeedAsync();
        await _store.MarkAsDeadLetterAsync(deadLetter, "broker rejected the payload");

        // days: 0 = 一天都不保留，两条都已过期；只有投递成功的那条该被删
        var deleted = await _store.DeleteExpiredEventsAsync(days: 0);

        Assert.Equal(1, deleted);
        Assert.Null(await _store.GetEventAsync(delivered));
        Assert.NotNull(await _store.GetEventAsync(deadLetter));
    }

    [Fact]
    public async Task DeleteExpiredEvents_KeepsUnprocessedEvents()
    {
        var pending = await SeedAsync();

        var deleted = await _store.DeleteExpiredEventsAsync(days: 0);

        Assert.Equal(0, deleted);
        Assert.NotNull(await _store.GetEventAsync(pending));
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
        _connection?.Dispose();
        _serviceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }

    public sealed class RetentionTestEvent : EventBase
    {
    }
}
