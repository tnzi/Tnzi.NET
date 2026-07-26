
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
