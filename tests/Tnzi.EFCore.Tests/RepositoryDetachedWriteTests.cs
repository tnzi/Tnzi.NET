using Tnzi.Storage;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 「只读查询读出来 → 改 → 写回」这条最常见的写法：仓储读方法默认 <c>AsNoTracking</c>，
/// 写回的实例是脱离跟踪的，附加进来时 EF 以它<b>当前</b>的值作原值快照。
/// </summary>
/// <remarks>
/// <para>
/// 原值即新值有两个直接后果，都由 <c>SaveChanges</c> 管线里「按原值与现值求差」的逻辑放大成静默缺陷：
/// </para>
/// <list type="bullet">
/// <item><b>文件引用</b>：<c>[FileField]</c> 的差集为空，新文件不建引用（开了清理就会被当孤儿物理删除），
/// 旧文件的引用永不释放（凭残留引用仍能读旧文件）。</item>
/// <item><b>软删审计</b>：<c>IsDeleted</c> 先被置 true 再附加，原值也是 true，「未删 → 已删」的跃迁判不出来，
/// 于是 <c>DeleterId</c> / <c>DeletionTime</c> 一列不写，正是一批「没有人、在没有时间删掉的」软删行。</item>
/// </list>
/// <para>
/// 真实 SQLite + <c>AddTnziDbContext</c> 装配 + 每个「请求」一个作用域；读回一律走新作用域。
/// </para>
/// </remarks>
public sealed class RepositoryDetachedWriteTests : IDisposable
{
    private readonly RepositoryWriteHost _host = new();

    public RepositoryDetachedWriteTests()
    {
        _host.User.SetUser(Guid.NewGuid(), "deleter");
    }

    public void Dispose() => _host.Dispose();

    // ── 文件引用 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_NoTrackingRead_ReplacedFileIsReportedAsDeleteAndCreate()
    {
        var oldFile = Guid.NewGuid();
        var newFile = Guid.NewGuid();
        var doc = new WriteHostDoc { Title = "contract", AttachmentId = oldFile };
        await _host.SeedAsync(doc);

        using (var scope = _host.CreateScope())
        {
            var repository = RepositoryWriteHost.Repository<WriteHostDoc>(scope);
            var detached = await repository.FirstOrDefaultAsync(d => d.Id == doc.Id);
            detached!.AttachmentId = newFile;

            await repository.UpdateAsync(detached);
        }

        AssertChanges(doc.Id, removed: [oldFile], added: [newFile]);
        Assert.Equal(newFile, (await _host.ReadAsync<WriteHostDoc>(doc.Id))!.AttachmentId);
    }

    [Fact]
    public async Task UpdateAsync_NoTrackingRead_ClearedFileIsReportedAsDelete()
    {
        var oldFile = Guid.NewGuid();
        var doc = new WriteHostDoc { Title = "contract", AttachmentId = oldFile };
        await _host.SeedAsync(doc);

        using (var scope = _host.CreateScope())
        {
            var repository = RepositoryWriteHost.Repository<WriteHostDoc>(scope);
            var detached = await repository.FirstOrDefaultAsync(d => d.Id == doc.Id);
            detached!.AttachmentId = null;

            await repository.UpdateAsync(detached);
        }

        AssertChanges(doc.Id, removed: [oldFile], added: []);
    }

    /// <summary>文件字段没动时不得凭空产生引用变更（回读原值不能制造假差集）。</summary>
    [Fact]
    public async Task UpdateAsync_NoTrackingRead_UntouchedFileProducesNoReferenceChange()
    {
        var doc = new WriteHostDoc { Title = "contract", AttachmentId = Guid.NewGuid() };
        await _host.SeedAsync(doc);

        using (var scope = _host.CreateScope())
        {
            var repository = RepositoryWriteHost.Repository<WriteHostDoc>(scope);
            var detached = await repository.FirstOrDefaultAsync(d => d.Id == doc.Id);
            detached!.Title = "renamed";

            await repository.UpdateAsync(detached);
        }

        Assert.Empty(_host.Processor.Changes);
        Assert.Equal("renamed", (await _host.ReadAsync<WriteHostDoc>(doc.Id))!.Title);
    }

    [Fact]
    public async Task UpdateManyAsync_NoTrackingRead_ReplacedFilesAreReported()
    {
        var first = new WriteHostDoc { Title = "a", AttachmentId = Guid.NewGuid() };
        var second = new WriteHostDoc { Title = "b", AttachmentId = Guid.NewGuid() };
        await _host.SeedAsync(first, second);
        var firstNew = Guid.NewGuid();
        var secondNew = Guid.NewGuid();

        using (var scope = _host.CreateScope())
        {
            var repository = RepositoryWriteHost.Repository<WriteHostDoc>(scope);
            var detached = await repository.GetListAsync([first.Id, second.Id]);
            detached.Single(d => d.Id == first.Id).AttachmentId = firstNew;
            detached.Single(d => d.Id == second.Id).AttachmentId = secondNew;

            await repository.UpdateManyAsync(detached);
        }

        AssertChanges(first.Id, removed: [first.AttachmentId!.Value], added: [firstNew]);
        AssertChanges(second.Id, removed: [second.AttachmentId!.Value], added: [secondNew]);
    }

    /// <summary>
    /// 回读的只是文件字段的原值：并发令牌的原值仍是调用方读到的那一版，过期写入照样被拦下。
    /// 整体回填原值会把令牌换成库里最新的那枚，过期读就绕过了乐观并发。
    /// </summary>
    [Fact]
    public async Task UpdateAsync_NoTrackingStaleRead_StillThrowsConcurrencyConflict()
    {
        var doc = new WriteHostDoc { Title = "v1", AttachmentId = Guid.NewGuid() };
        await _host.SeedAsync(doc);

        using var scope = _host.CreateScope();
        var repository = RepositoryWriteHost.Repository<WriteHostDoc>(scope);
        var stale = await repository.FirstOrDefaultAsync(d => d.Id == doc.Id);

        using (var other = _host.CreateScope())
        {
            var otherRepository = RepositoryWriteHost.Repository<WriteHostDoc>(other);
            var fresh = await otherRepository.GetAsync(doc.Id);
            fresh!.Title = "v2 elsewhere";
            await otherRepository.UpdateAsync(fresh);
        }

        stale!.AttachmentId = Guid.NewGuid();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => repository.UpdateAsync(stale));
    }

    // ── 软删审计 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_NoTrackingRead_WritesDeleterAndDeletionTime()
    {
        var doc = new WriteHostDoc { Title = "to delete" };
        await _host.SeedAsync(doc);

        using (var scope = _host.CreateScope())
        {
            var repository = RepositoryWriteHost.Repository<WriteHostDoc>(scope);
            var detached = await repository.FirstOrDefaultAsync(d => d.Id == doc.Id);

            await repository.DeleteAsync(detached!);
        }

        await AssertSoftDeletedByCurrentUserAsync(doc.Id);
    }

    /// <summary>带跟踪读出的路径（<c>GetAsync</c>）行为不变。</summary>
    [Fact]
    public async Task DeleteAsync_TrackedRead_WritesDeleterAndDeletionTime()
    {
        var doc = new WriteHostDoc { Title = "to delete" };
        await _host.SeedAsync(doc);

        using (var scope = _host.CreateScope())
        {
            var repository = RepositoryWriteHost.Repository<WriteHostDoc>(scope);
            var tracked = await repository.GetAsync(doc.Id);

            await repository.DeleteAsync(tracked!);
        }

        await AssertSoftDeletedByCurrentUserAsync(doc.Id);
    }

    /// <summary>同作用域里这一行已被跟踪，又拿一份不跟踪的副本来删：合并到已跟踪的那一条，照样写删除人。</summary>
    [Fact]
    public async Task DeleteAsync_DetachedCopyOfTrackedRow_WritesDeleter()
    {
        var doc = new WriteHostDoc { Title = "to delete" };
        await _host.SeedAsync(doc);

        using (var scope = _host.CreateScope())
        {
            var repository = RepositoryWriteHost.Repository<WriteHostDoc>(scope);
            _ = await repository.GetAsync(doc.Id);
            var copy = await repository.FirstOrDefaultAsync(d => d.Id == doc.Id);

            await repository.DeleteAsync(copy!);
            Assert.True(copy!.IsDeleted);
        }

        await AssertSoftDeletedByCurrentUserAsync(doc.Id);
    }

    /// <summary>
    /// 少于优化阈值（逐个走变更跟踪器）与多于阈值（数据库端批量更新）两条路径必须写出同样的删除人。
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(25)]
    public async Task DeleteManyAsync_NoTrackingRead_WritesDeleterOnEveryRow(int count)
    {
        var docs = Enumerable.Range(0, count).Select(i => new WriteHostDoc { Title = $"doc-{i}" }).ToArray();
        await _host.SeedAsync(docs);

        using (var scope = _host.CreateScope())
        {
            var repository = RepositoryWriteHost.Repository<WriteHostDoc>(scope);
            var detached = await repository.GetListAsync(docs.Select(d => d.Id));

            await repository.DeleteManyAsync(detached);
        }

        foreach (var doc in docs)
        {
            await AssertSoftDeletedByCurrentUserAsync(doc.Id);
        }
    }

    /// <summary>批量里的不跟踪副本与同作用域已跟踪的行同主键：不得抛「同主键实例已被跟踪」。</summary>
    [Fact]
    public async Task DeleteManyAsync_DetachedCopiesOfTrackedRows_DoNotThrow()
    {
        var first = new WriteHostDoc { Title = "a" };
        var second = new WriteHostDoc { Title = "b" };
        await _host.SeedAsync(first, second);

        using (var scope = _host.CreateScope())
        {
            var repository = RepositoryWriteHost.Repository<WriteHostDoc>(scope);
            _ = await repository.GetAsync(first.Id);
            var copies = await repository.GetListAsync([first.Id, second.Id]);

            await repository.DeleteManyAsync(copies);
        }

        await AssertSoftDeletedByCurrentUserAsync(first.Id);
        await AssertSoftDeletedByCurrentUserAsync(second.Id);
    }

    /// <summary>
    /// 刚新增、尚未落库的实体被业务删除：它从未存在过，不插入（与硬删、<c>DbContext.Remove</c> 同一语义），
    /// 而不是被降级成 <c>UPDATE</c> 影响 0 行后抛并发异常，也不是插进一行带删除标记的垃圾数据。
    /// </summary>
    [Fact]
    public async Task DeleteManyAsync_JustInsertedInTheSameTransaction_IsNeverInserted()
    {
        var kept = new WriteHostDoc { Title = "kept" };
        var discarded = new WriteHostDoc { Title = "discarded" };

        using (var scope = _host.CreateScope())
        {
            var repository = RepositoryWriteHost.Repository<WriteHostDoc>(scope);
            var manager = RepositoryWriteHost.Manager(scope);
            manager.EnableTransaction();

            await repository.InsertAsync(kept);
            await repository.InsertAsync(discarded);
            await repository.DeleteManyAsync([discarded]);

            await manager.CommitTransactionAsync();
        }

        Assert.NotNull(await _host.ReadAsync<WriteHostDoc>(kept.Id));
        Assert.Null(await _host.ReadAsync<WriteHostDoc>(discarded.Id));
    }

    [Fact]
    public async Task DeleteAsync_JustInsertedInTheSameTransaction_IsNeverInserted()
    {
        var discarded = new WriteHostDoc { Title = "discarded" };

        using (var scope = _host.CreateScope())
        {
            var repository = RepositoryWriteHost.Repository<WriteHostDoc>(scope);
            var manager = RepositoryWriteHost.Manager(scope);
            manager.EnableTransaction();

            await repository.InsertAsync(discarded);
            await repository.DeleteAsync(discarded);

            await manager.CommitTransactionAsync();
        }

        Assert.Null(await _host.ReadAsync<WriteHostDoc>(discarded.Id));
    }

    /// <summary>
    /// 多于优化阈值、其中混着本事务里刚新增的实体：数据库端批量 SQL 碰不到它，它也不得带着删除标记被插进库。
    /// </summary>
    [Fact]
    public async Task DeleteManyAsync_ManyIncludingJustInserted_NeverInsertsTheNewOne()
    {
        var existing = Enumerable.Range(0, 22).Select(i => new WriteHostDoc { Title = $"doc-{i}" }).ToArray();
        await _host.SeedAsync(existing);
        var justInserted = new WriteHostDoc { Title = "just inserted" };

        using (var scope = _host.CreateScope())
        {
            var repository = RepositoryWriteHost.Repository<WriteHostDoc>(scope);
            var manager = RepositoryWriteHost.Manager(scope);
            manager.EnableTransaction();

            await repository.InsertAsync(justInserted);
            var detached = await repository.GetListAsync(existing.Select(d => d.Id));
            await repository.DeleteManyAsync([.. detached, justInserted]);

            await manager.CommitTransactionAsync();
        }

        Assert.Null(await _host.ReadAsync<WriteHostDoc>(justInserted.Id));
        foreach (var doc in existing)
        {
            await AssertSoftDeletedByCurrentUserAsync(doc.Id);
        }
    }

    /// <summary>
    /// 多于优化阈值、实例是带跟踪读出的：删完之后同作用域里的下一次保存不得因这些实例撞并发冲突
    /// （批量 SQL 换了库里的并发戳，跟踪器里还是旧的）。
    /// </summary>
    [Fact]
    public async Task DeleteManyAsync_ManyTrackedRows_LeavesTheScopeUsable()
    {
        var docs = Enumerable.Range(0, 25).Select(i => new WriteHostDoc { Title = $"doc-{i}" }).ToArray();
        await _host.SeedAsync(docs);
        var later = new WriteHostDoc { Title = "written after the delete" };

        using (var scope = _host.CreateScope())
        {
            var repository = RepositoryWriteHost.Repository<WriteHostDoc>(scope);
            var tracked = await repository.AsQueryable(withTracking: true).ToListAsync();

            await repository.DeleteManyAsync(tracked);
            await repository.InsertAsync(later);
        }

        Assert.NotNull(await _host.ReadAsync<WriteHostDoc>(later.Id));
        foreach (var doc in docs)
        {
            await AssertSoftDeletedByCurrentUserAsync(doc.Id);
        }
    }

    // ── 断言 ───────────────────────────────────────────────────────────────

    private async Task AssertSoftDeletedByCurrentUserAsync(Guid id)
    {
        var stored = await _host.ReadAsync<WriteHostDoc>(id);
        Assert.NotNull(stored);
        Assert.True(stored!.IsDeleted, $"Row {id} is not soft-deleted");
        Assert.Equal(_host.User.Id, stored.DeleterId);
        Assert.NotNull(stored.DeletionTime);
    }

    private void AssertChanges(Guid entityId, Guid[] removed, Guid[] added)
    {
        var changes = _host.Processor.Changes.Where(c => c.EntityId == entityId.ToString()).ToList();
        Assert.Equal(
            removed.OrderBy(g => g),
            changes.Where(c => c.ChangeType == FileReferenceChangeType.Delete).Select(c => c.FileId).OrderBy(g => g));
        Assert.Equal(
            added.OrderBy(g => g),
            changes.Where(c => c.ChangeType == FileReferenceChangeType.Create).Select(c => c.FileId).OrderBy(g => g));
    }
}
