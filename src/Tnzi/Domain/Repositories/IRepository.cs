
namespace Tnzi.Domain.Repositories;

/// <summary>
/// 仓储接口定义（读写）
/// 继承 IReadOnlyRepository 的所有查询能力，并添加写入操作
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
[StableApi(Since = "0.1.0")]
public interface IRepository<TEntity> : IReadOnlyRepository<TEntity>
    where TEntity : class, IEntity
{
    // 写入操作
    Task InsertAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task InsertManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task UpdateManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task DeleteManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task DeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flush pending changes for this repository's underlying DbContext.
    /// Useful when callers need a previously-staged Insert/Update to be visible to
    /// subsequent queries within the same UnitOfWork transaction - by default the
    /// repository defers SaveChanges when a transaction is enabled, so writes only
    /// land at commit time. Default implementation is a no-op; EF Core repository overrides.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

    /// <summary>
    /// 确保当前工作单元的物理数据库事务已开启（幂等）。
    /// 在事务内执行绕过变更跟踪的裸 SQL 操作（ExecuteUpdate/ExecuteDelete/Raw SQL）前
    /// MUST 调用本方法使其加入事务；物理事务默认延迟到首次 SaveChanges 才开启，
    /// 不调用则裸 SQL 在自动提交模式下执行（行锁不持有到事务结束、回滚无法撤销）。
    /// 未启用事务时为无操作。默认实现为 no-op；EF Core 仓储覆盖实现。
    /// </summary>
    Task EnsureTransactionStartedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// 丢弃一个<b>插入/更新失败</b>的实体，使它不再参与本工作单元后续的任何 SaveChanges。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ <b>为什么需要它</b>：<c>InsertAsync</c> 之后 <c>SaveChangesAsync</c> 抛异常（典型是撞上
    /// 唯一索引），实体<b>仍然留在变更跟踪器里</b>。调用方即便把异常当成正常分支吞掉，
    /// 那个实体也会被本作用域内<b>下一次</b> SaveChanges 重新提交并再次抛出 ——
    /// 而那一次的异常往往在完全无关的位置、被完全不同的 catch（或没有 catch）接住。
    /// </para>
    /// <para>
    /// 症状因此非常难认：吞异常只挡住了第一跳。框架内已三次撞上同一形态
    /// （连续编号首插竞态、银行流水导入的并发去重、周期性单据的幂等键命中），
    /// 其中银行流水那处的表现是「一次并发碰撞之后，本批剩下的每一行都被计成已跳过，
    /// 而一条都没真的导进去」——<b>报告成功的静默数据丢失</b>。
    /// </para>
    /// <para>
    /// 与 <see cref="DeleteAsync(TEntity, CancellationToken)"/> <b>不是</b>一回事：删除是业务动作
    /// （对软删实体会写 <c>IsDeleted</c> 并落库，对一个从未存在过的行来说那是凭空造一条垃圾数据）；
    /// 本方法只操作变更跟踪状态，<b>不产生任何 SQL</b>。
    /// </para>
    /// <para>
    /// 默认实现为 no-op：没有变更跟踪器的仓储实现本来就没有东西要丢弃，这是真答案而非降级。
    /// EF Core 仓储覆盖实现。
    /// </para>
    /// </remarks>
    void Discard(TEntity entity)
    {
    }
}

/// <summary>
/// 带主键的仓储接口（读写）
/// </summary>
[StableApi(Since = "0.1.0")]
public interface IRepository<TEntity, TKey> : IRepository<TEntity>, IReadOnlyRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// 删除实体
    /// </summary>
    Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 部分更新实体
    /// </summary>
    Task UpdateAsync(TKey id, Action<TEntity> updateAction, CancellationToken cancellationToken = default);
}
