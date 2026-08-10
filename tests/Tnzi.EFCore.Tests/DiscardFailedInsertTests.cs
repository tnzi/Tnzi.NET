namespace Tnzi.EFCore.Tests;

/// <summary>
/// <c>IRepository.Discard</c>：丢弃一个<b>插入失败</b>的实体。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>为什么需要这个原语</b>：插入失败（典型是撞唯一索引）之后，实体<b>仍然留在变更跟踪器里、
/// 仍然是 <c>Added</c></b>。调用方即便把异常当成正常分支吞掉（「这一条已经有人做过了，跳过」），
/// 那个实体也会被本作用域内<b>下一次</b> SaveChanges 重新提交并再次抛出 ——
/// 而那一次的异常出现在完全无关的位置、被完全不同的 catch（或没有 catch）接住。
/// </para>
/// <para>
/// <b>吞掉异常只挡住了第一跳。</b> 框架内已三次撞上同一形态：
/// </para>
/// <list type="bullet">
/// <item><c>DocumentNumberService</c> 首插竞态（当时靠 <c>DeleteAsync</c> 兜住，
/// 那条实体恰好不是软删实体所以侥幸可行）。</item>
/// <item><c>BankStatementIngestor</c> 的并发去重 —— 表现是<b>一次碰撞之后本批剩下的每一行
/// 都被计成已跳过、而一条都没真的导进去</b>，返回值看起来完全正常（报告成功的静默数据丢失）。</item>
/// <item><c>RecurringGeneratorService</c> 的幂等键命中 —— 重放出的 <c>DbUpdateException</c>
/// 冲出扫描循环，把本轮剩下的模板全部弄死。</item>
/// </list>
/// <para>
/// ★ 用例里用 <c>ChangeTracker.Clear()</c> 模拟「第一行是<b>别的</b>进程/作用域插入的」——
/// 那正是真实触发条件（本作用域的 <c>existing</c> 快照里没有它）。不清跟踪器的话，
/// 同 key 的第二个实例会在 <c>Add</c> 阶段就被 EF 拦住，根本走不到 SaveChanges，
/// 也就复现不出这个问题。
/// </para>
/// </remarks>
public class DiscardFailedInsertTests : EFCoreTestBase
{
    private readonly EFCoreRepository<TestDbContext, TestEntityWithGuidId, Guid> _repository;
    private readonly EFCoreRepository<TestDbContext, TestSoftDeletableProduct, Guid> _softDeleteRepository;

    public DiscardFailedInsertTests()
    {
        _repository = new EFCoreRepository<TestDbContext, TestEntityWithGuidId, Guid>(DbContext, null, ServiceProvider);
        _softDeleteRepository = new EFCoreRepository<TestDbContext, TestSoftDeletableProduct, Guid>(DbContext, null, ServiceProvider);
    }

    /// <summary>
    /// ★ 不 Discard 时，失败的插入会被<b>下一次无关的</b>写入重放。
    /// </summary>
    /// <remarks>
    /// 这条是整个问题的存在性证明：第二次异常发生在写 <c>later</c> 的时候，
    /// 而 <c>later</c> 本身完全合法。任何在此处 catch 的代码都会把原因归给错误的那一行。
    /// </remarks>
    [Fact]
    public async Task WithoutDiscard_TheFailedInsertIsReplayedByTheNextWrite()
    {
        var duplicate = await SeedThenPrepareCollidingInsertAsync();

        // 一条**全新且合法**的实体，却存不进去 —— 因为上面那条失败的实体又被提交了一次
        var later = new TestEntityWithGuidId { Id = Guid.NewGuid(), Name = "later" };
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => _repository.InsertAsync(later));

        Assert.Equal(EntityState.Added, DbContext.Entry(duplicate).State);
    }

    /// <summary>Discard 之后，后续写入照常成功，而那条从未成功过的实体绝不会落库。</summary>
    [Fact]
    public async Task AfterDiscard_TheNextWriteSucceeds_AndTheFailedRowNeverLands()
    {
        var duplicate = await SeedThenPrepareCollidingInsertAsync();

        _repository.Discard(duplicate);
        Assert.Equal(EntityState.Detached, DbContext.Entry(duplicate).State);

        var later = new TestEntityWithGuidId { Id = Guid.NewGuid(), Name = "later" };
        await _repository.InsertAsync(later);

        DbContext.ChangeTracker.Clear();
        var names = await DbContext.Set<TestEntityWithGuidId>().AsNoTracking().Select(r => r.Name).ToListAsync();
        Assert.Equal(2, names.Count);
        Assert.Contains("first", names);
        Assert.Contains("later", names);
        Assert.DoesNotContain("duplicate", names);
    }

    /// <summary>已经 Detached 的实体再 Discard 一次是幂等的（错误恢复路径不该挑剔调用次数）。</summary>
    [Fact]
    public void Discard_IsIdempotent()
    {
        var entity = new TestEntityWithGuidId { Id = Guid.NewGuid(), Name = "x" };

        _repository.Discard(entity);
        _repository.Discard(entity);

        Assert.Equal(EntityState.Detached, DbContext.Entry(entity).State);
    }

    /// <summary>
    /// ★★ 为什么不能用 <c>DeleteAsync</c> 代替：软删实体会被<b>真的插进库里</b>。
    /// </summary>
    /// <remarks>
    /// 这正是 <c>BankStatementIngestor</c>（<c>BankTransaction</c> 是软删实体）如果照抄
    /// <c>DocumentNumberService</c> 的 <c>DeleteAsync</c> 写法会发生的事：
    /// 一条从未成功过的插入，变成库里一行带着 <c>IsDeleted = true</c> 的垃圾数据，
    /// 而软删实体的过滤唯一索引排除已删行，所以它连报错都不会。
    /// <para>
    /// 这里用 <c>DbContext.Add</c> 直接把实体置为 <c>Added</c>（不落库），
    /// 因为要验的恰恰是「<c>Added</c> 状态下这两个方法的行为差别」。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task DeleteAsync_IsNotASubstitute_ItPersistsAnAddedSoftDeleteEntity()
    {
        var viaDelete = new TestSoftDeletableProduct { Id = Guid.NewGuid(), Name = "via delete" };
        DbContext.Add(viaDelete);

        // 业务删除：软删实体走「置 IsDeleted 再 Update」，而 Added 状态下那就是一次 INSERT
        await _softDeleteRepository.DeleteAsync(viaDelete);

        var afterDelete = await CountAllSoftDeletableAsync();
        Assert.True(afterDelete == 1,
            "DeleteAsync 把一条从未成功过的插入写进了库里 —— 这就是不能用它代替 Discard 的理由");

        var viaDiscard = new TestSoftDeletableProduct { Id = Guid.NewGuid(), Name = "via discard" };
        DbContext.Add(viaDiscard);
        _softDeleteRepository.Discard(viaDiscard);
        await DbContext.SaveChangesAsync();

        Assert.True(await CountAllSoftDeletableAsync() == 1, "Discard 之后不该多出任何一行");
    }

    // ── 夹具 ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 插一行 "first"，清跟踪器（= 那行是别人插的），再造一条同 key 的插入并断言它失败。
    /// </summary>
    private async Task<TestEntityWithGuidId> SeedThenPrepareCollidingInsertAsync()
    {
        var id = Guid.NewGuid();
        await _repository.InsertAsync(new TestEntityWithGuidId { Id = id, Name = "first" });
        DbContext.ChangeTracker.Clear();

        var duplicate = new TestEntityWithGuidId { Id = id, Name = "duplicate" };
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => _repository.InsertAsync(duplicate));
        return duplicate;
    }

    private async Task<int> CountAllSoftDeletableAsync()
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<TestSoftDeletableProduct>()
            .IgnoreQueryFilters().AsNoTracking().CountAsync();
    }
}
