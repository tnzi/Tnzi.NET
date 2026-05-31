using Tnzi.AI.Channels.Entities;
using Tnzi.AI.Channels.Entities.Configs;
using Tnzi.AI.Channels.Store;

namespace Tnzi.AI.Tests.Channels;

/// <summary>
/// Test DbContext exposing the ChannelThreadMapping entity with its unique
/// (ChannelName, ChatId, TopicId) index so the SQLite provider enforces it.
/// </summary>
public class ChannelThreadStoreDbContext : TnziDbContext<ChannelThreadStoreDbContext>
{
    public ChannelThreadStoreDbContext(
        DbContextOptions<ChannelThreadStoreDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ChannelThreadMappingConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// B8 — verifies DatabaseChannelThreadStore.SetThreadIdAsync survives the TOCTOU race
/// where two concurrent first-messages for the same (channel, chat, topic) key both
/// pass the read-then-insert check and the second insert hits the unique index.
/// </summary>
public class DatabaseChannelThreadStoreTests : IntegratedTestBase<ChannelThreadStoreDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<ChannelThreadMapping, Guid>,
            EFCoreRepository<ChannelThreadStoreDbContext, ChannelThreadMapping, Guid>>();
    }

    private DatabaseChannelThreadStore CreateStore()
    {
        var repo = ServiceProvider.GetRequiredService<IRepository<ChannelThreadMapping, Guid>>();
        return new DatabaseChannelThreadStore(repo);
    }

    [Fact]
    public async Task SetThreadIdAsync_ConcurrentInsertSameKey_NoExceptionEscapes_OneRow()
    {
        // Arrange — two stores over two independent scopes/DbContexts (mimics ChannelManager
        // dispatching parallel Task.Run with per-scope DbContexts).
        using var scope1 = ServiceProvider.CreateScope();
        using var scope2 = ServiceProvider.CreateScope();
        var store1 = new DatabaseChannelThreadStore(scope1.ServiceProvider.GetRequiredService<IRepository<ChannelThreadMapping, Guid>>());
        var store2 = new DatabaseChannelThreadStore(scope2.ServiceProvider.GetRequiredService<IRepository<ChannelThreadMapping, Guid>>());

        var threadA = Guid.NewGuid();
        var threadB = Guid.NewGuid();

        // Act — both attempt to create the mapping for the same key at once.
        var t1 = store1.SetThreadIdAsync("telegram", "chat-1", threadA, topicId: null, userId: "u1");
        var t2 = store2.SetThreadIdAsync("telegram", "chat-1", threadB, topicId: null, userId: "u2");

        // Assert — neither call throws (unique violation is absorbed by upsert-with-retry).
        await Should.NotThrowAsync(async () => await Task.WhenAll(t1, t2));

        // Exactly one row exists for the key.
        var rows = await DbContext.Set<ChannelThreadMapping>()
            .Where(m => m.ChannelName == "telegram" && m.ChatId == "chat-1" && m.TopicId == null)
            .ToListAsync();
        rows.Count.ShouldBe(1);

        // Both callers can read back the same (now-existing) threadId.
        var resolved = await CreateStore().GetThreadIdAsync("telegram", "chat-1");
        resolved.ShouldNotBeNull();
        resolved.Value.ShouldBeOneOf(threadA, threadB);
    }

    [Fact]
    public async Task SetThreadIdAsync_LosingInsert_DoesNotPoisonDbContext()
    {
        // Arrange — pre-seed the row so the next insert is guaranteed to collide.
        var existingThread = Guid.NewGuid();
        await CreateStore().SetThreadIdAsync("slack", "C123", existingThread, topicId: null, userId: "owner");

        // Act — a second store (fresh DbContext) reads stale (empty), then tries to insert.
        using var scope = ServiceProvider.CreateScope();
        var store = new DatabaseChannelThreadStore(scope.ServiceProvider.GetRequiredService<IRepository<ChannelThreadMapping, Guid>>());

        // Force the read-then-insert path: this store has never seen the row in its own context.
        await Should.NotThrowAsync(async () =>
            await store.SetThreadIdAsync("slack", "C123", Guid.NewGuid(), topicId: null, userId: "racer"));

        // The DbContext is not poisoned — a subsequent unrelated write succeeds.
        await Should.NotThrowAsync(async () =>
            await store.SetThreadIdAsync("slack", "C999", Guid.NewGuid(), topicId: null, userId: "other"));

        // Still exactly one row for the contended key.
        var rows = await DbContext.Set<ChannelThreadMapping>()
            .Where(m => m.ChannelName == "slack" && m.ChatId == "C123")
            .ToListAsync();
        rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SetThreadIdAsync_ExistingRow_UpdatesInPlace()
    {
        // Arrange
        var store = CreateStore();
        var first = Guid.NewGuid();
        await store.SetThreadIdAsync("discord", "G1", first, topicId: null, userId: "u1");

        // Act — second call for the same key updates rather than inserts.
        var second = Guid.NewGuid();
        await store.SetThreadIdAsync("discord", "G1", second, topicId: null, userId: "u1");

        // Assert
        var rows = await DbContext.Set<ChannelThreadMapping>()
            .Where(m => m.ChannelName == "discord" && m.ChatId == "G1")
            .ToListAsync();
        rows.Count.ShouldBe(1);
        rows[0].ThreadId.ShouldBe(second);
    }
}
