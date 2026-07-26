using Microsoft.Data.Sqlite;
using Tnzi.AI.Infrastructure.Memory;
using Tnzi.AI.Memory;

namespace Tnzi.AI.Tests.Memory;

/// <summary>
/// MemoryEntry 精确重复硬防线集成测试 - (Scope, ContentHash) 过滤唯一索引 +
/// DatabaseMemoryStore 写入路径的哈希填充与插入前查重。
/// 使用 SQLite 内存库 + 真实 EFCoreRepository（镜像 AgentGrantServiceTests 的自包含模式），
/// 因为唯一索引约束必须经过真实的 DDL/写入管道验证。
/// </summary>
public class DatabaseMemoryStoreDedupTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly MemoryDedupDbContext _context;
    private readonly DatabaseMemoryStore _store;
    private readonly IRepository<MemoryEntry, Guid> _repository;

    public DatabaseMemoryStoreDedupTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(m => m.Id).Returns(Guid.Empty);
        currentUserMock.Setup(m => m.IsAuthenticated).Returns(false);

        var options = new DbContextOptionsBuilder<MemoryDedupDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new MemoryDedupDbContext(options, currentUserMock.Object);
        _context.Database.EnsureCreated();

        _repository = new EFCoreRepository<MemoryDedupDbContext, MemoryEntry, Guid>(_context);
        _store = new DatabaseMemoryStore(_repository, Mock.Of<ILogger<DatabaseMemoryStore>>());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    // =====================================================================
    // 1. 重复写只落一行（Append 路径 - 插入前查重）
    // =====================================================================

    [Fact]
    public async Task AppendAsync_DuplicateContentSameScope_OnlyOneRowPersisted()
    {
        await _store.AppendAsync("dedup-scope", "User prefers dark mode");
        _context.ChangeTracker.Clear();
        await _store.AppendAsync("dedup-scope", "User prefers dark mode");
        _context.ChangeTracker.Clear();

        var rows = await _context.Set<MemoryEntry>()
            .Where(e => e.Scope == "dedup-scope")
            .ToListAsync();

        rows.Count.ShouldBe(1);
        rows[0].ContentHash.ShouldNotBeNull();
        rows[0].ContentHash!.Length.ShouldBe(64);
        // 第二次写入命中重复 → 顺带更新访问记录
        rows[0].AccessCount.ShouldBe(1);
        rows[0].LastAccessedTime.ShouldNotBeNull();
    }

    [Fact]
    public async Task AppendAsync_WithMetadata_DuplicateSkipped_ConsolidatorAddPath()
    {
        // consolidator 的 ADD 决策走 AppendAsync(scope, entry, importance, category) 重载
        await _store.AppendAsync("consolidate-scope", "fact: API budget is 100 USD", 0.8, "fact");
        _context.ChangeTracker.Clear();
        await _store.AppendAsync("consolidate-scope", "fact: API budget is 100 USD", 0.9, "fact");
        _context.ChangeTracker.Clear();

        var rows = await _context.Set<MemoryEntry>()
            .Where(e => e.Scope == "consolidate-scope")
            .ToListAsync();

        rows.Count.ShouldBe(1);
        rows[0].Importance.ShouldBe(0.8); // 原行保留，未被重复写覆盖
    }

    // =====================================================================
    // 2. 不同 scope 同内容互不影响
    // =====================================================================

    [Fact]
    public async Task AppendAsync_SameContentDifferentScopes_BothPersist()
    {
        await _store.AppendAsync("scope-a", "shared knowledge");
        _context.ChangeTracker.Clear();
        await _store.AppendAsync("scope-b", "shared knowledge");
        _context.ChangeTracker.Clear();

        var rows = await _context.Set<MemoryEntry>()
            .Where(e => e.Content == "shared knowledge")
            .ToListAsync();

        rows.Count.ShouldBe(2);
        rows.Select(r => r.Scope).ShouldBe(new[] { "scope-a", "scope-b" }, ignoreOrder: true);
        rows[0].ContentHash.ShouldBe(rows[1].ContentHash); // 哈希相同但 scope 不同 → 不冲突
    }

    [Fact]
    public async Task AppendAsync_MemoryScopeOverload_AgentIsolationViaScopeKey()
    {
        // MemoryScope 键编码 agent 维度 → 不同 Agent 的同内容互不影响
        var agentA = Guid.NewGuid();
        var agentB = Guid.NewGuid();

        await _store.AppendAsync(MemoryScope.ForAgent(agentA), "agent likes brevity");
        _context.ChangeTracker.Clear();
        await _store.AppendAsync(MemoryScope.ForAgent(agentB), "agent likes brevity");
        _context.ChangeTracker.Clear();
        // 同 Agent 重复 → 跳过
        await _store.AppendAsync(MemoryScope.ForAgent(agentA), "agent likes brevity");
        _context.ChangeTracker.Clear();

        var rows = await _context.Set<MemoryEntry>()
            .Where(e => e.Content == "agent likes brevity")
            .ToListAsync();

        rows.Count.ShouldBe(2);
    }

    // =====================================================================
    // 3. 唯一索引兜底（绕过 store 的 DB 级约束验证）
    // =====================================================================

    [Fact]
    public async Task UniqueIndex_BlocksRawDuplicateInsert()
    {
        var hash = MemoryContentHasher.Compute("raw duplicate");
        _context.Set<MemoryEntry>().Add(new MemoryEntry { Scope = "raw-scope", Content = "raw duplicate", ContentHash = hash });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        _context.Set<MemoryEntry>().Add(new MemoryEntry { Scope = "raw-scope", Content = "raw duplicate", ContentHash = hash });

        await Should.ThrowAsync<DbUpdateException>(() => _context.SaveChangesAsync());
        _context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task UniqueIndex_NullContentHash_LegacyRowsNotConstrained()
    {
        // 既有行（无哈希）不参与唯一约束 - 过滤索引排除 NULL
        _context.Set<MemoryEntry>().AddRange(
            new MemoryEntry { Scope = "legacy", Content = "same legacy content", ContentHash = null },
            new MemoryEntry { Scope = "legacy", Content = "same legacy content", ContentHash = null });

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        (await _context.Set<MemoryEntry>().CountAsync(e => e.Scope == "legacy")).ShouldBe(2);
    }

    // =====================================================================
    // 4. Write/Update 路径填充哈希
    // =====================================================================

    [Fact]
    public async Task WriteAsync_PopulatesContentHash()
    {
        await _store.WriteAsync("write-scope", "replacement content");
        _context.ChangeTracker.Clear();

        var row = await _context.Set<MemoryEntry>().SingleAsync(e => e.Scope == "write-scope");
        row.ContentHash.ShouldBe(MemoryContentHasher.Compute("replacement content"));
    }

    [Fact]
    public async Task UpdateEntryAsync_RecomputesContentHash()
    {
        await _store.AppendAsync("update-scope", "before");
        _context.ChangeTracker.Clear();
        var row = await _context.Set<MemoryEntry>().SingleAsync(e => e.Scope == "update-scope");
        _context.ChangeTracker.Clear();

        await _store.UpdateEntryAsync("update-scope", row.Id, "after");
        _context.ChangeTracker.Clear();

        var updated = await _context.Set<MemoryEntry>().SingleAsync(e => e.Id == row.Id);
        updated.Content.ShouldBe("after");
        updated.ContentHash.ShouldBe(MemoryContentHasher.Compute("after"));
    }

    // =====================================================================
    // 5. AgentMemoryService（admin 写路径）- 重复创建 409
    // =====================================================================

    [Fact]
    public async Task AgentMemoryService_CreateDuplicate_Returns409()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        var serviceProvider = new ServiceCollection().AddLogging().BuildServiceProvider();
        var service = new AgentMemoryService(_repository, serviceProvider);
        var agentId = Guid.NewGuid();

        var first = await service.CreateAsync(agentId, new CreateAgentMemoryDto { Content = "curated fact" });
        first.Succeeded.ShouldBeTrue(first.Message);
        _context.ChangeTracker.Clear();

        var second = await service.CreateAsync(agentId, new CreateAgentMemoryDto { Content = "curated fact" });
        second.Succeeded.ShouldBeFalse();
        second.Code.ShouldBe(409);
        _context.ChangeTracker.Clear();

        (await _context.Set<MemoryEntry>().CountAsync(e => e.AgentId == agentId)).ShouldBe(1);
    }
}

/// <summary>
/// 测试专用 DbContext - 仅注册 MemoryEntry（含 (Scope, ContentHash) 过滤唯一索引）。
/// </summary>
internal sealed class MemoryDedupDbContext : TnziDbContext<MemoryDedupDbContext>
{
    public MemoryDedupDbContext(
        DbContextOptions<MemoryDedupDbContext> options,
        ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new MemoryEntryConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
