namespace Tnzi.EFCore.Tests;

/// <summary>
/// <c>TrackedDuplicateResolver.Merge</c> 对并发戳（<see cref="IConcurrencyStamp"/>）实体的语义。
/// </summary>
/// <remarks>
/// <para>
/// ★ EF 生成 <c>UPDATE ... WHERE ConcurrencyStamp = @p</c> 时用的是条目的 <b>OriginalValue</b>。
/// 合并只把传入实例的值写进 CurrentValues 而不动 OriginalValues，等于拿已跟踪条目上一次加载时的旧戳去比：
/// 「同作用域先跟踪过这一行 → 别的作用域中途改过（戳变了）→ 本作用域 <c>AsNoTracking</c> 读到新戳的实例、
/// 改字段 → <c>UpdateAsync</c>」这条序列里，调用方明明是基于数据库最新状态做的修改，
/// 却照样撞 <c>DbUpdateConcurrencyException</c>。伪冲突在 Finance 那些 catch 里会被当成真冲突回 409，其它地方 500。
/// </para>
/// <para>
/// 传入实例是刚从数据库读出来的，它带的戳至少与已跟踪条目一样新；把并发令牌的 OriginalValue 同步成传入值，
/// WHERE 才比对得到调用方真正看见过的那一版。真冲突（传入实例本身是过期读）仍然要抛。
/// </para>
/// <para>
/// 第二个作用域用同一条 SQLite 内存连接上的另一个 <c>DbContext</c> 模拟；「另一个请求改过这一行」就是这个意思。
/// </para>
/// </remarks>
public class UpdateTrackedDuplicateConcurrencyTests : EFCoreTestBase
{
    private readonly EFCoreRepository<TestDbContext, TestAuditedDocument, Guid> _repository;

    public UpdateTrackedDuplicateConcurrencyTests()
    {
        _repository = new EFCoreRepository<TestDbContext, TestAuditedDocument, Guid>(DbContext, null, ServiceProvider);
    }

    [Fact]
    public async Task UpdateAsync_IncomingReadAfterForeignWrite_SucceedsWithTheNewerStamp()
    {
        var tracked = await SeedAndKeepTrackedAsync();
        var staleStamp = tracked.ConcurrencyStamp;

        var foreignStamp = await ForeignScopeRenamesAsync(tracked.Id, "renamed elsewhere");
        Assert.NotEqual(staleStamp, foreignStamp);

        // 本作用域重新读到的是数据库最新的那一版（戳已经是别人写的那个）
        var latest = await ReadDetachedCopyAsync(tracked.Id);
        Assert.Equal(foreignStamp, latest.ConcurrencyStamp);
        latest.Title = "renamed here on top of the latest row";

        await _repository.UpdateAsync(latest);

        var persisted = await ReadDetachedCopyAsync(tracked.Id);
        Assert.Equal("renamed here on top of the latest row", persisted.Title);
        // 保存换了一枚新戳：既不是过期的那枚，也不是别人写的那枚
        Assert.NotEqual(staleStamp, persisted.ConcurrencyStamp);
        Assert.NotEqual(foreignStamp, persisted.ConcurrencyStamp);
        // 已跟踪的那一条仍是权威实例，合并方向 detached → tracked
        Assert.Same(tracked, DbContext.Set<TestAuditedDocument>().Local.Single(d => d.Id == tracked.Id));
        Assert.Equal(persisted.ConcurrencyStamp, tracked.ConcurrencyStamp);
    }

    [Fact]
    public async Task UpdateAsync_IncomingIsAStaleRead_StillThrowsConcurrencyException()
    {
        var tracked = await SeedAndKeepTrackedAsync();

        // 传入实例先读（戳 A），别人再写（戳 B）：这是真冲突，合并不能把它放过去
        var stale = await ReadDetachedCopyAsync(tracked.Id);
        await ForeignScopeRenamesAsync(tracked.Id, "renamed elsewhere");
        stale.Title = "built on a stale row";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => _repository.UpdateAsync(stale));
    }

    [Fact]
    public async Task UpdateManyAsync_IncomingReadAfterForeignWrite_SucceedsWithTheNewerStamp()
    {
        var tracked = await SeedAndKeepTrackedAsync();
        var foreignStamp = await ForeignScopeRenamesAsync(tracked.Id, "renamed elsewhere");

        var latest = await ReadDetachedCopyAsync(tracked.Id);
        Assert.Equal(foreignStamp, latest.ConcurrencyStamp);
        latest.Title = "batch on top of the latest row";

        await _repository.UpdateManyAsync([latest]);

        var persisted = await ReadDetachedCopyAsync(tracked.Id);
        Assert.Equal("batch on top of the latest row", persisted.Title);
        Assert.NotEqual(foreignStamp, persisted.ConcurrencyStamp);
    }

    [Fact]
    public async Task UpdateManyAsync_IncomingIsAStaleRead_StillThrowsConcurrencyException()
    {
        var tracked = await SeedAndKeepTrackedAsync();
        var stale = await ReadDetachedCopyAsync(tracked.Id);
        await ForeignScopeRenamesAsync(tracked.Id, "renamed elsewhere");
        stale.Title = "batch built on a stale row";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => _repository.UpdateManyAsync([stale]));
    }

    /// <summary>
    /// 已跟踪的那一条还是 Added（尚未落库）时合并保持 INSERT：INSERT 没有 WHERE，并发戳的 OriginalValue 不参与，
    /// 传入实例上随便写的戳不能把 INSERT 变成一条影响 0 行的 UPDATE。
    /// </summary>
    [Fact]
    public async Task UpdateAsync_TrackedRowIsAdded_KeepsInsertSemantics()
    {
        var doc = new TestAuditedDocument { Id = Guid.NewGuid(), Title = "pending insert" };
        DbContext.Set<TestAuditedDocument>().Add(doc);
        Assert.Equal(EntityState.Added, DbContext.Entry(doc).State);

        var detached = new TestAuditedDocument { Id = doc.Id, Title = "merged before insert", ConcurrencyStamp = "incoming" };
        await _repository.UpdateAsync(detached);

        Assert.Same(doc, DbContext.Set<TestAuditedDocument>().Local.Single(d => d.Id == doc.Id));
        var persisted = await ReadDetachedCopyAsync(doc.Id);
        Assert.Equal("merged before insert", persisted.Title);
    }

    private async Task<TestAuditedDocument> SeedAndKeepTrackedAsync()
    {
        var doc = new TestAuditedDocument { Title = "original" };
        await _repository.InsertAsync(doc);
        Assert.NotEqual(EntityState.Detached, DbContext.Entry(doc).State);
        Assert.False(string.IsNullOrEmpty(doc.ConcurrencyStamp));
        return doc;
    }

    private async Task<TestAuditedDocument> ReadDetachedCopyAsync(Guid id)
    {
        var copy = await DbContext.Set<TestAuditedDocument>().AsNoTracking().SingleAsync(d => d.Id == id);
        Assert.Equal(EntityState.Detached, DbContext.Entry(copy).State);
        return copy;
    }

    /// <summary>另一个作用域（同一连接上的另一个 DbContext）改这一行并保存，返回它写下的新戳。</summary>
    private async Task<string> ForeignScopeRenamesAsync(Guid id, string title)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(DbContext.Database.GetDbConnection())
            .Options;
        await using var foreign = new TestDbContext(options, new MockCurrentUser());
        var row = await foreign.Set<TestAuditedDocument>().SingleAsync(d => d.Id == id);
        row.Title = title;
        await foreign.SaveChangesAsync();
        return row.ConcurrencyStamp;
    }
}
