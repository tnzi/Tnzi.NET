
namespace Tnzi.EFCore.Extensions;

/// <summary>
/// 批量操作扩展方法
/// 使用 EF Core 8+ 原生批量更新/删除 API
/// </summary>
/// <remarks>
/// <para>
/// 批量更新操作请直接使用 EF Core 原生 API:
/// <code>
/// await dbContext.Set&lt;Artist&gt;()
///     .Where(a => a.Country == "Austria")
///     .ExecuteUpdateAsync(s => s
///         .SetProperty(a => a.Country, "Germany")
///         .SetProperty(a => a.UpdatedAt, DateTime.UtcNow));
/// </code>
/// </para>
/// </remarks>
public static class BatchOperationExtensions
{
    #region IQueryable 扩展

    /// <summary>
    /// 批量删除（IQueryable 扩展）
    /// 不加载实体到内存，直接执行 SQL DELETE
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    /// <param name="query">查询对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>受影响的行数</returns>
    /// <example>
    /// <code>
    /// var count = await repository
    ///     .Where(a => a.IsActive == false)
    ///     .BatchDeleteAsync();
    /// </code>
    /// </example>
    public static Task<int> BatchDeleteAsync<T>(
        this IQueryable<T> query,
        CancellationToken cancellationToken = default) where T : class
    {
        return query.ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// 批量软删除（IQueryable 扩展）
    /// 将 IsDeleted 设置为 true
    /// </summary>
    /// <typeparam name="T">实体类型（必须实现 ISoftDelete）</typeparam>
    /// <param name="query">查询对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>受影响的行数</returns>
    /// <example>
    /// <code>
    /// var count = await repository
    ///     .Where(a => a.IsActive == false)
    ///     .BatchSoftDeleteAsync();
    /// </code>
    /// </example>
    public static Task<int> BatchSoftDeleteAsync<T>(
        this IQueryable<T> query,
        CancellationToken cancellationToken = default)
        where T : class, ISoftDelete
    {
        return query.ExecuteUpdateAsync(
            s => s.SetProperty(e => e.IsDeleted, true),
            cancellationToken);
    }

    #endregion

    #region DbSet 扩展

    /// <summary>
    /// 批量删除符合条件的实体（硬删除）
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    /// <param name="dbSet">DbSet</param>
    /// <param name="predicate">筛选条件</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>受影响的行数</returns>
    /// <example>
    /// <code>
    /// var count = await dbContext.Artists.BatchDeleteAsync(
    ///     a => a.Country == "Unknown");
    /// </code>
    /// </example>
    public static Task<int> BatchDeleteAsync<T>(
        this DbSet<T> dbSet,
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default) where T : class
    {
        return dbSet.Where(predicate).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// 批量软删除符合条件的实体
    /// 将 IsDeleted 设置为 true
    /// </summary>
    /// <typeparam name="T">实体类型（必须实现 ISoftDelete）</typeparam>
    /// <param name="dbSet">DbSet</param>
    /// <param name="predicate">筛选条件</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>受影响的行数</returns>
    /// <example>
    /// <code>
    /// var count = await dbContext.Artists.BatchSoftDeleteAsync(
    ///     a => a.CreatedAt &lt; DateTime.UtcNow.AddYears(-1));
    /// </code>
    /// </example>
    public static Task<int> BatchSoftDeleteAsync<T>(
        this DbSet<T> dbSet,
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        where T : class, ISoftDelete
    {
        return dbSet.Where(predicate)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsDeleted, true), cancellationToken);
    }

    #endregion
}