
namespace Tnzi.Domain.Repositories;

/// <summary>
/// 只读仓储接口
/// 提供纯查询能力，不包含写入操作，适用于 CQRS 查询端
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
[StableApi(Since = "0.1.0")]
public interface IReadOnlyRepository<TEntity> : IQueryable<TEntity>
    where TEntity : class, IEntity
{
    // 基础查询
    Task<TEntity?> FindAsync(params object[] keys);
    Task<TEntity?> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    // 便捷查询方法（默认使用 AsNoTracking 以提升性能）
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);
    Task<TEntity?> SingleOrDefaultAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);
    Task<bool> AnyAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);
    Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);
    Task<long> LongCountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取实体（不存在抛异常）
    /// </summary>
    Task<TEntity> GetRequiredAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<List<TEntity>> ToListAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);
    Task<List<TEntity>> ToCachedListAsync(Expression<Func<TEntity, bool>> predicate, string cacheKey, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    Task<TEntity[]> ToArrayAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 IQueryable 查询对象
    /// </summary>
    IQueryable<TEntity> AsQueryable(bool withTracking = false);

    /// <summary>
    /// 投影查询
    /// </summary>
    Task<List<TResult>> SelectAsync<TResult>(Expression<Func<TEntity, TResult>> selector, Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);

    // 分页查询
    Task<IPagedList<TEntity>> GetPagedListAsync(PagedQuery query, CancellationToken cancellationToken = default);
    Task<IPagedList<TEntity>> GetPagedListAsync(Expression<Func<TEntity, bool>> predicate, PagedQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 投影分页查询
    /// </summary>
    Task<IPagedList<TResult>> GetPagedListAsync<TResult>(Expression<Func<TEntity, TResult>> selector, PagedQuery query, Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);

    // Dynamic filter methods (using default interface methods for backward compatibility)

    /// <summary>
    /// Get a filtered paged list using dynamic FilterGroup.
    /// </summary>
    Task<IPagedList<TEntity>> GetFilteredPagedListAsync(
        FilterGroup filter, PagedQuery query,
        Expression<Func<TEntity, bool>>? additionalPredicate = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("GetFilteredPagedListAsync is not supported by this repository implementation.");

    /// <summary>
    /// Get a filtered list using dynamic FilterGroup (non-paged).
    /// </summary>
    Task<List<TEntity>> GetFilteredListAsync(
        FilterGroup filter, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("GetFilteredListAsync is not supported by this repository implementation.");

    /// <summary>
    /// Count entities matching a dynamic FilterGroup.
    /// </summary>
    Task<int> CountAsync(
        FilterGroup filter, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("CountAsync(FilterGroup) is not supported by this repository implementation.");

    // 聚合方法（使用 default interface methods 以保持向后兼容）

    /// <summary>
    /// 求和
    /// </summary>
    Task<decimal> SumAsync(Expression<Func<TEntity, decimal>> selector, Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("SumAsync is not supported by this repository implementation.");

    /// <summary>
    /// 求最小值
    /// </summary>
    Task<TResult> MinAsync<TResult>(Expression<Func<TEntity, TResult>> selector, Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("MinAsync is not supported by this repository implementation.");

    /// <summary>
    /// 求最大值
    /// </summary>
    Task<TResult> MaxAsync<TResult>(Expression<Func<TEntity, TResult>> selector, Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("MaxAsync is not supported by this repository implementation.");

    /// <summary>
    /// 求平均值
    /// </summary>
    Task<decimal> AverageAsync(Expression<Func<TEntity, decimal>> selector, Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("AverageAsync is not supported by this repository implementation.");
}

/// <summary>
/// 带主键的只读仓储接口
/// </summary>
[StableApi(Since = "0.1.0")]
public interface IReadOnlyRepository<TEntity, TKey> : IReadOnlyRepository<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// 获取实体（不存在返回 null）
    /// </summary>
    Task<TEntity?> GetAsync(TKey id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取实体（不存在抛异常）
    /// </summary>
    Task<TEntity> GetRequiredAsync(TKey id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量获取实体
    /// </summary>
    Task<List<TEntity>> GetListAsync(IEnumerable<TKey> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取实体并加载关联数据
    /// </summary>
    Task<TEntity?> GetWithDetailsAsync(TKey id, CancellationToken cancellationToken = default, params Expression<Func<TEntity, object>>[] includes);

    // 便捷查询方法
    Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default);
    Task<Dictionary<TKey, TEntity>> ToDictionaryAsync(CancellationToken cancellationToken = default);
    Task<Dictionary<TKey, TEntity>> ToDictionaryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
}
