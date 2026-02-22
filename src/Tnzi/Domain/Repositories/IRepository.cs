
namespace Tnzi.Domain.Repositories;

/// <summary>
/// 仓储接口定义
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
[StableApi(Since = "0.1.0")]
public interface IRepository<TEntity> : IQueryable<TEntity> 
    where TEntity : class, IEntity
{
    // 基础查询
    Task<TEntity?> FindAsync(params object[] keys);
    Task<TEntity?> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    
    // 便捷查询方法（默认使用AsNoTracking以提升性能）
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);
    Task<TEntity?> SingleOrDefaultAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);
    [Obsolete("Use ExistsAsync instead")]
    Task<bool> AnyAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
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
/// 带主键的仓储接口
/// </summary>
[StableApi(Since = "0.1.0")]
public interface IRepository<TEntity, TKey> : IRepository<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    /// 获取实体（不存在返回null）
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
    /// 删除实体
    /// </summary>
    Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 部分更新实体
    /// </summary>
    Task UpdateAsync(TKey id, Action<TEntity> updateAction, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取实体并加载关联数据
    /// </summary>
    Task<TEntity?> GetWithDetailsAsync(TKey id, CancellationToken cancellationToken = default, params Expression<Func<TEntity, object>>[] includes);

    // 便捷查询方法
    Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default);
    Task<Dictionary<TKey, TEntity>> ToDictionaryAsync(CancellationToken cancellationToken = default);
    Task<Dictionary<TKey, TEntity>> ToDictionaryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
}