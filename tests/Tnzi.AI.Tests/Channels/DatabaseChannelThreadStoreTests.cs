using System.Reflection;
using Tnzi.AI.Channels.Entities;
using Tnzi.AI.Channels.Events.Handlers;
using Tnzi.AI.Channels.Entities.Configs;
using Tnzi.AI.Channels.Store;

namespace Tnzi.AI.Tests.Channels;

/// <summary>
/// Test DbContext exposing the ChannelThreadMapping entity with its unique
/// (ChannelName, ChatId, TopicId) indexes so the SQLite provider enforces them.
/// </summary>
/// <remarks>
/// 经 <c>RegisterTo(modelBuilder, this)</c> 注册而不是裸 <c>ApplyConfiguration</c>：配置要读到
/// DbContext 才认得出数据库提供者与多租户开关，与运行期的自动注册走同一条路。
/// </remarks>
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
        new ChannelThreadMappingConfiguration().RegisterTo(modelBuilder, this);
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// 多租户开启时的同一张表（单独的上下文类型：EF 按上下文类型缓存模型）。
/// </summary>
public class ChannelThreadStoreMultiTenantDbContext : TnziDbContext<ChannelThreadStoreMultiTenantDbContext>
{
    public ChannelThreadStoreMultiTenantDbContext(
        DbContextOptions<ChannelThreadStoreMultiTenantDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new ChannelThreadMappingConfiguration().RegisterTo(modelBuilder, this);
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// B8 - verifies DatabaseChannelThreadStore.SetThreadIdAsync survives the TOCTOU race
/// where two concurrent first-messages for the same (channel, chat, topic) key both
/// pass the read-then-insert check and the second insert hits the unique index.
/// </summary>
/// <remarks>
/// ★ 绝大多数映射的 <c>TopicId</c> 是 null（只有 Telegram 论坛话题带它）。PostgreSQL / SQLite / MySQL
/// 认为唯一索引里的 NULL 互不相等，SQL Server 上 EF 又会给可空列的唯一索引自动加 <c>IS NOT NULL</c> 过滤，
/// 一条含 TopicId 的唯一索引在<b>任何</b>库上都挡不住两行 (slack, D123, NULL)。这里的用例跑在真实 SQLite 上，
/// 正是为了把「索引在 NULL 那一支上是否成立」钉住。
/// </remarks>
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
        // Arrange - two stores over two independent scopes/DbContexts (mimics ChannelManager
        // dispatching parallel Task.Run with per-scope DbContexts).
        using var scope1 = ServiceProvider.CreateScope();
        using var scope2 = ServiceProvider.CreateScope();
        var store1 = new DatabaseChannelThreadStore(scope1.ServiceProvider.GetRequiredService<IRepository<ChannelThreadMapping, Guid>>());
        var store2 = new DatabaseChannelThreadStore(scope2.ServiceProvider.GetRequiredService<IRepository<ChannelThreadMapping, Guid>>());

        var threadA = Guid.NewGuid();
        var threadB = Guid.NewGuid();

        // Act - both attempt to create the mapping for the same key at once.
        var t1 = store1.SetThreadIdAsync("telegram", "chat-1", threadA, topicId: null, userId: "u1");
        var t2 = store2.SetThreadIdAsync("telegram", "chat-1", threadB, topicId: null, userId: "u2");

        // Assert - neither call throws (unique violation is absorbed by upsert-with-retry).
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
    public async Task UniqueIndex_RejectsASecondRowForTheSameChatWithoutATopic()
    {
        // 数据库是最终把关者：服务层的判重是 read-then-write，两个并发首条消息都会读到「还没有」。
        DbContext.Set<ChannelThreadMapping>().Add(Mapping("slack", "D123", topicId: null));
        await DbContext.SaveChangesAsync();

        using var scope = ServiceProvider.CreateScope();
        var other = scope.ServiceProvider.GetRequiredService<ChannelThreadStoreDbContext>();
        other.Set<ChannelThreadMapping>().Add(Mapping("slack", "D123", topicId: null));

        await Should.ThrowAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task UniqueIndex_TopicRows_SameTopicRejected_OtherTopicsAndTheBareChatAllowed()
    {
        DbContext.Set<ChannelThreadMapping>().Add(Mapping("telegram", "chat-1", topicId: "t1"));
        DbContext.Set<ChannelThreadMapping>().Add(Mapping("telegram", "chat-1", topicId: "t2"));
        DbContext.Set<ChannelThreadMapping>().Add(Mapping("telegram", "chat-1", topicId: null));
        await DbContext.SaveChangesAsync();

        using var scope = ServiceProvider.CreateScope();
        var other = scope.ServiceProvider.GetRequiredService<ChannelThreadStoreDbContext>();
        other.Set<ChannelThreadMapping>().Add(Mapping("telegram", "chat-1", topicId: "t1"));

        await Should.ThrowAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task SetThreadIdAsync_LosingInsert_ResolvesToAnUpdate_AndDoesNotPoisonDbContext()
    {
        // Arrange - 另一个作用域先落下这一行。
        var existingThread = Guid.NewGuid();
        await CreateStore().SetThreadIdAsync("slack", "C123", existingThread, topicId: null, userId: "owner");

        // 这个 store 的「先读」看不见那一行，与两条并发首条消息在同一瞬间读库时一模一样，
        // 于是它必然走插入分支，必然撞上唯一索引，必须由 catch 分支改判为更新。
        using var scope = ServiceProvider.CreateScope();
        var repository = StaleFirstReadRepository.Wrap(
            scope.ServiceProvider.GetRequiredService<IRepository<ChannelThreadMapping, Guid>>());
        var store = new DatabaseChannelThreadStore(repository);

        var racerThread = Guid.NewGuid();
        await Should.NotThrowAsync(async () =>
            await store.SetThreadIdAsync("slack", "C123", racerThread, topicId: null, userId: "racer"));

        // The DbContext is not poisoned - a subsequent unrelated write succeeds.
        await Should.NotThrowAsync(async () =>
            await store.SetThreadIdAsync("slack", "C999", Guid.NewGuid(), topicId: null, userId: "other"));

        // Still exactly one row for the contended key, and it carries the later write.
        var rows = await DbContext.Set<ChannelThreadMapping>().AsNoTracking()
            .Where(m => m.ChannelName == "slack" && m.ChatId == "C123")
            .ToListAsync();
        rows.Count.ShouldBe(1);
        rows[0].ThreadId.ShouldBe(racerThread);
    }

    [Fact]
    public async Task SetThreadIdAsync_ExistingRow_UpdatesInPlace()
    {
        // Arrange
        var store = CreateStore();
        var first = Guid.NewGuid();
        await store.SetThreadIdAsync("discord", "G1", first, topicId: null, userId: "u1");

        // Act - second call for the same key updates rather than inserts.
        var second = Guid.NewGuid();
        await store.SetThreadIdAsync("discord", "G1", second, topicId: null, userId: "u1");

        // Assert
        var rows = await DbContext.Set<ChannelThreadMapping>()
            .Where(m => m.ChannelName == "discord" && m.ChatId == "G1")
            .ToListAsync();
        rows.Count.ShouldBe(1);
        rows[0].ThreadId.ShouldBe(second);
    }

    [Fact]
    public async Task ThreadDeleted_RemovesEveryMappingThatPointsAtTheThread_AndNothingElse()
    {
        // 管理员删了一条线程：指向它的会话映射必须当场收走，否则那个会话下一条消息被运行以「线程不存在」拒绝
        var deleted = Guid.NewGuid();
        var kept = Guid.NewGuid();
        var store = CreateStore();
        await store.SetThreadIdAsync("telegram", "c1", deleted);
        await store.SetThreadIdAsync("telegram", "c1", deleted, topicId: "42");
        await store.SetThreadIdAsync("slack", "D9", kept);

        var handler = new ChannelThreadMappingCleanupHandler(store);
        await handler.HandleAsync(new ThreadDeletedEvent { ThreadId = deleted });

        (await store.GetThreadIdAsync("telegram", "c1")).ShouldBeNull();
        (await store.GetThreadIdAsync("telegram", "c1", "42")).ShouldBeNull();
        (await store.GetThreadIdAsync("slack", "D9")).ShouldBe(kept);
    }

    internal static ChannelThreadMapping Mapping(string channel, string chatId, string? topicId, Guid? tenantId = null) => new()
    {
        ChannelName = channel,
        ChatId = chatId,
        TopicId = topicId,
        ThreadId = Guid.NewGuid(),
        TenantId = tenantId
    };
}

/// <summary>
/// 多租户开启时，同一把唯一键要在「有租户」与「宿主（TenantId 为 NULL）」两支上都成立。
/// </summary>
public class DatabaseChannelThreadStoreMultiTenantTests : IntegratedTestBase<ChannelThreadStoreMultiTenantDbContext>
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.UseTnziMultiTenancy(true);

    [Fact]
    public async Task HostRows_SecondRowForTheSameChatWithoutATopic_Rejected()
    {
        // 未归属租户的渠道（适配器 TenantId 为 null）写下的是宿主行，NULL 租户同样逃过含 TenantId 的唯一索引
        DbContext.Set<ChannelThreadMapping>().Add(DatabaseChannelThreadStoreTests.Mapping("slack", "D123", null, tenantId: null));
        await DbContext.SaveChangesAsync();

        using var scope = ServiceProvider.CreateScope();
        var other = scope.ServiceProvider.GetRequiredService<ChannelThreadStoreMultiTenantDbContext>();
        other.Set<ChannelThreadMapping>().Add(DatabaseChannelThreadStoreTests.Mapping("slack", "D123", null, tenantId: null));

        await Should.ThrowAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task TenantRows_SameTenantRejected_DifferentTenantsAllowed()
    {
        DbContext.Set<ChannelThreadMapping>().Add(DatabaseChannelThreadStoreTests.Mapping("slack", "D123", null, TenantA));
        DbContext.Set<ChannelThreadMapping>().Add(DatabaseChannelThreadStoreTests.Mapping("slack", "D123", null, TenantB));
        DbContext.Set<ChannelThreadMapping>().Add(DatabaseChannelThreadStoreTests.Mapping("slack", "D123", "t1", TenantA));
        await DbContext.SaveChangesAsync();

        using var scope = ServiceProvider.CreateScope();
        var other = scope.ServiceProvider.GetRequiredService<ChannelThreadStoreMultiTenantDbContext>();
        other.Set<ChannelThreadMapping>().Add(DatabaseChannelThreadStoreTests.Mapping("slack", "D123", null, TenantA));

        await Should.ThrowAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }
}

/// <summary>
/// 唯一索引修正之前的库：同一把键下可能已经躺着重复行（直到消费方的迁移去重）。
/// 这个上下文去掉了唯一索引，用来重现那种存量数据。
/// </summary>
public class LegacyChannelThreadStoreDbContext : TnziDbContext<LegacyChannelThreadStoreDbContext>
{
    public LegacyChannelThreadStoreDbContext(
        DbContextOptions<LegacyChannelThreadStoreDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        new ChannelThreadMappingConfiguration().RegisterTo(modelBuilder, this);
        base.OnModelCreating(modelBuilder);

        // 在基类之后删：基类的实体自动注册可能把同一份配置再登记一遍（取决于进程里先建过哪个上下文），
        // 先删后登记会让唯一索引回来，这组用例就随运行顺序时红时绿。
        var entityType = modelBuilder.Entity<ChannelThreadMapping>().Metadata;
        foreach (var index in entityType.GetIndexes().Where(i => i.IsUnique).ToList())
        {
            entityType.RemoveIndex(index);
        }

        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// 存量重复行：读写都必须落在同一行（最早写下的那一行），移除必须一行不剩。
/// </summary>
public class DatabaseChannelThreadStoreLegacyDuplicateTests : IntegratedTestBase<LegacyChannelThreadStoreDbContext>
{
    private static readonly DateTime Earlier = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<ChannelThreadMapping, Guid>,
            EFCoreRepository<LegacyChannelThreadStoreDbContext, ChannelThreadMapping, Guid>>();
    }

    private DatabaseChannelThreadStore CreateStore()
        => new(ServiceProvider.GetRequiredService<IRepository<ChannelThreadMapping, Guid>>());

    /// <summary>后写的那一行先落盘：不排序时按物理顺序会先读到它，排序后读到的必须是最早的那一行。</summary>
    private async Task<(Guid Earliest, Guid Latest)> SeedDuplicatesAsync()
    {
        var latest = DatabaseChannelThreadStoreTests.Mapping("slack", "D123", topicId: null);
        latest.CreationTime = Later;
        DbContext.Set<ChannelThreadMapping>().Add(latest);
        await DbContext.SaveChangesAsync();

        var earliest = DatabaseChannelThreadStoreTests.Mapping("slack", "D123", topicId: null);
        earliest.CreationTime = Earlier;
        DbContext.Set<ChannelThreadMapping>().Add(earliest);
        await DbContext.SaveChangesAsync();

        DbContext.ChangeTracker.Clear();
        return (earliest.ThreadId, latest.ThreadId);
    }

    [Fact]
    public async Task GetThreadIdAsync_WithLegacyDuplicates_ReadsTheEarliestRow()
    {
        var (earliest, _) = await SeedDuplicatesAsync();

        (await CreateStore().GetThreadIdAsync("slack", "D123")).ShouldBe(earliest);
    }

    [Fact]
    public async Task SetThreadIdAsync_WithLegacyDuplicates_WritesTheRowThatIsReadBack()
    {
        await SeedDuplicatesAsync();
        var fresh = Guid.NewGuid();

        await CreateStore().SetThreadIdAsync("slack", "D123", fresh);

        (await CreateStore().GetThreadIdAsync("slack", "D123")).ShouldBe(fresh);
    }

    [Fact]
    public async Task RemoveAsync_WithLegacyDuplicates_RemovesEveryRowForTheKey()
    {
        await SeedDuplicatesAsync();

        await CreateStore().RemoveAsync("slack", "D123");

        (await CreateStore().GetThreadIdAsync("slack", "D123")).ShouldBeNull();
        (await DbContext.Set<ChannelThreadMapping>().CountAsync()).ShouldBe(0);
    }
}

/// <summary>
/// 让被包装仓储的<b>第一次</b>跟踪查询读不到任何行，其余调用原样转发。
/// 用来确定性地重现「两个作用域同时读到『还没有』」的竞态窗口。
/// </summary>
public class StaleFirstReadRepository : DispatchProxy
{
    private IRepository<ChannelThreadMapping, Guid> _inner = null!;
    private bool _staleReadServed;

    public static IRepository<ChannelThreadMapping, Guid> Wrap(IRepository<ChannelThreadMapping, Guid> inner)
    {
        var proxy = Create<IRepository<ChannelThreadMapping, Guid>, StaleFirstReadRepository>();
        ((StaleFirstReadRepository)(object)proxy)._inner = inner;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var result = targetMethod!.Invoke(_inner, args);

        if (!_staleReadServed
            && targetMethod.Name == nameof(IRepository<ChannelThreadMapping, Guid>.AsQueryable)
            && args is [true])
        {
            _staleReadServed = true;
            return ((IQueryable<ChannelThreadMapping>)result!).Where(_ => false);
        }

        return result;
    }
}
