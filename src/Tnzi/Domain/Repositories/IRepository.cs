
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
