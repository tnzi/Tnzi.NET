namespace Tnzi.EFCore.Tests;

/// <summary>
/// <c>UpdateAsync</c> / <c>UpdateManyAsync</c>：传入的实例与变更跟踪器里已有的条目同主键。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>为什么这是仓储的责任</b>：框架里最常见的写法是「只读查询读出来 → 改 → <c>UpdateAsync</c>」。
/// 而 <c>FindAsync(predicate)</c> / <c>FirstOrDefaultAsync</c> 一类只读查询一律走 <c>AsNoTracking</c>，
/// 拿到的必然是<b>脱离跟踪的新实例</b>。同一个作用域内第二次读到同一行（重试、批量里出现重复项、
/// 或者这一行本来就是刚刚在这个作用域里写进去的），<c>Attach</c> 就会撞上已跟踪的那一条，
/// 抛 <c>InvalidOperationException: another instance with the same key value is already being tracked</c>。
/// </para>
/// <para>
/// 调用方<b>无从预防</b>：<c>IRepository</c> 不暴露变更跟踪器，也没有任何签名或文档说过
/// 「同一个作用域里同一行只能写一次」。所以收口只能在仓储：把值合并到已跟踪的那一条。
/// </para>
/// <para>
/// ★ 用例里用 <c>ChangeTracker.Clear()</c> + 重新查询来造出「第二个实例」，
/// 这与真实触发路径等价（真实路径是 <c>AsNoTracking</c> 查询本身产出新实例）。
/// </para>
/// </remarks>
public class UpdateTrackedDuplicateTests : EFCoreTestBase
{
    private readonly EFCoreRepository<TestDbContext, TestProduct, Guid> _repository;

    public UpdateTrackedDuplicateTests()
    {
        _repository = new EFCoreRepository<TestDbContext, TestProduct, Guid>(DbContext, null, ServiceProvider);
    }

    [Fact]
    public async Task UpdateAsync_DetachedInstanceOfTrackedRow_MergesInsteadOfThrowing()
    {
        var tracked = await SeedAndKeepTrackedAsync();
        var detached = await ReadDetachedCopyAsync(tracked.Id);

        detached.Name = "renamed";
        detached.Stock = 7;

        await _repository.UpdateAsync(detached);

        // 已跟踪的那一条是权威实例，别处可能正持有它的引用，所以合并方向只能是 detached → tracked。
        Assert.Equal("renamed", tracked.Name);
        Assert.Equal(7, tracked.Stock);
        Assert.Same(tracked, DbContext.Set<TestProduct>().Local.Single(p => p.Id == tracked.Id));

        var persisted = await DbContext.Set<TestProduct>().AsNoTracking().SingleAsync(p => p.Id == tracked.Id);
        Assert.Equal("renamed", persisted.Name);
        Assert.Equal(7, persisted.Stock);
    }

    /// <summary>
    /// 已跟踪的那一条带着尚未落库的改动时，合并不能把它冲掉：
    /// 累加型的字段（引用计数、余额）在这上面栽了才是真正的静默错误。
    /// </summary>
    [Fact]
    public async Task UpdateAsync_MergeKeepsWritingToTheTrackedInstance()
    {
        var tracked = await SeedAndKeepTrackedAsync();
        var detached = await ReadDetachedCopyAsync(tracked.Id);

        detached.Stock += 1;
        await _repository.UpdateAsync(detached);

        // 第二轮：调用方继续在**已跟踪**的那一条上累加（真实场景里它由带跟踪的查询拿到）。
        tracked.Stock += 1;
        await _repository.UpdateAsync(tracked);

        var persisted = await DbContext.Set<TestProduct>().AsNoTracking().SingleAsync(p => p.Id == tracked.Id);
        Assert.Equal(3, persisted.Stock);
    }

    [Fact]
    public async Task UpdateManyAsync_DetachedInstanceOfTrackedRow_MergesInsteadOfThrowing()
    {
        var tracked = await SeedAndKeepTrackedAsync();
        var detached = await ReadDetachedCopyAsync(tracked.Id);
        detached.Name = "batch-renamed";

        // 对照组：同样脱离跟踪，但变更跟踪器里没有同主键的条目，走普通的 UpdateRange。
        // 只脱开它自己（不能 ChangeTracker.Clear()，那会把上面那条已跟踪的也清掉，
        // 冲突就不成立了 —— 用例也就再也抓不到回归）。
        var untouched = new TestProduct { Name = "other", Price = 2m, Stock = 5 };
        await _repository.InsertAsync(untouched);
        DbContext.Entry(untouched).State = EntityState.Detached;
        untouched.Name = "other-renamed";

        // 同一批里既有"与已跟踪条目撞主键"的，也有普通的脱离跟踪实例：两者都要写进去。
        await _repository.UpdateManyAsync([detached, untouched]);

        var products = await DbContext.Set<TestProduct>().AsNoTracking().ToListAsync();
        Assert.Equal("batch-renamed", products.Single(p => p.Id == tracked.Id).Name);
        Assert.Equal("other-renamed", products.Single(p => p.Id == untouched.Id).Name);
    }

    /// <summary>
    /// 同主键的条目处于 <c>Deleted</c> 时刻意<b>不</b>走合并：删除语义不在这次收口范围内。
    /// </summary>
    /// <remarks>
    /// ★ 这条分支本来就<b>不抛</b>：实测 EF 的 <c>Attach</c> 会把 <c>Deleted</c> 条目顶掉，
    /// 待删的那一行变成被更新（<b>删除被静默取消</b>）。这是本次改动之前就存在的行为，
    /// 用例在此把它钉住，好让"合并已跟踪条目"这个改动不会顺手换掉删除语义 ——
    /// 换一种静默行为只是把问题挪个地方。这条行为本身值得单独决策，不在本次范围内。
    /// </remarks>
    [Fact]
    public async Task UpdateAsync_TrackedRowIsDeleted_KeepsPreExistingBehaviour()
    {
        var tracked = await SeedAndKeepTrackedAsync();
        var detached = await ReadDetachedCopyAsync(tracked.Id);

        DbContext.Set<TestProduct>().Remove(tracked);
        Assert.Equal(EntityState.Deleted, DbContext.Entry(tracked).State);

        detached.Name = "resurrected";
        await _repository.UpdateAsync(detached);

        // 合并没有介入：写进去的是传入的那个实例，不是已跟踪的那一条。
        Assert.Equal("original", tracked.Name);

        var persisted = await DbContext.Set<TestProduct>().AsNoTracking().SingleAsync(p => p.Id == detached.Id);
        Assert.Equal("resurrected", persisted.Name);
    }

    /// <summary>
    /// 主键还是默认值时不做合并：那是一条尚未定型的新行，
    /// 与已跟踪的行合并只会把两条不相干的记录并成一条。
    /// </summary>
    [Fact]
    public async Task UpdateAsync_UnsetKey_DoesNotMergeIntoTrackedRow()
    {
        var tracked = await SeedAndKeepTrackedAsync();
        var keyless = new TestProduct { Name = "unsaved", Price = 9m, Stock = 99 };

        await _repository.UpdateAsync(keyless);

        Assert.NotEqual("unsaved", tracked.Name);
        Assert.NotEqual(99, tracked.Stock);
    }

    private async Task<TestProduct> SeedAndKeepTrackedAsync()
    {
        var product = new TestProduct { Name = "original", Price = 1m, Stock = 1 };
        await _repository.InsertAsync(product);

        // InsertAsync 之后实体仍留在变更跟踪器里 —— 这正是真实触发条件。
        Assert.NotEqual(EntityState.Detached, DbContext.Entry(product).State);
        return product;
    }

    private async Task<TestProduct> ReadDetachedCopyAsync(Guid id)
    {
        var copy = await DbContext.Set<TestProduct>().AsNoTracking().SingleAsync(p => p.Id == id);
        Assert.Equal(EntityState.Detached, DbContext.Entry(copy).State);
        return copy;
    }
}
