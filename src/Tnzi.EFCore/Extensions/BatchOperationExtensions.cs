
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
    /// 批量软删除（IQueryable 扩展）：与仓储 <c>DeleteAsync(predicate)</c> 产出逐列相同的软删行
    /// （IsDeleted、删除人、删除时间、修改人、修改时间、并发戳，按实体实现的接口决定）。
    /// </summary>
    /// <remarks>
    /// 删除人与时间取自应用容器里的 <see cref="ICurrentUser"/> 与 <see cref="TimeProvider"/>，
    /// 只有查询根暴露 DbContext —— <c>DbSet&lt;T&gt;</c> 本身，或仓储的 <c>AsQueryable(withTracking: true)</c>（对已是 IQueryable 的 DbSet 原样返回）；
    /// 仓储缺省的 <c>AsQueryable()</c> 已经组合了 <c>AsNoTracking()</c>，与 Where 之后的组合查询一样，EF Core 没有公开途径从它拿到 DbContext。
    /// 实体带有删除人 / 修改人字段而当前用户解析不到（组合查询，或手工构造的 DbContext 没有应用容器）时**抛出**，
    /// 请改用 <c>BatchSoftDeleteByAsync(deleterId, deletionTime)</c> —— 静默少写几列正是这条扩展此前的缺陷。
    /// 不在此强开工作单元的物理事务：这组 IQueryable 扩展由调用方自管事务（见 <c>EnsureTransactionStartedAsync</c>）。
    /// </remarks>
    /// <typeparam name="T">实体类型（必须实现 ISoftDelete）</typeparam>
    /// <param name="query">查询对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>受影响的行数</returns>
    /// <example>
    /// <code>
    /// // 查询根：删除人与时间自动解析
    /// var count = await dbContext.Set&lt;Artist&gt;().BatchSoftDeleteAsync(a => a.IsActive == false);
    /// // 组合查询：EF 拿不到 DbContext，须显式给出删除人与时间
    /// var count = await query.Where(a => a.IsActive == false).BatchSoftDeleteByAsync(CurrentUser.Id, now);
    /// </code>
    /// </example>
    public static Task<int> BatchSoftDeleteAsync<T>(
        this IQueryable<T> query,
        CancellationToken cancellationToken = default)
        where T : class, ISoftDelete
    {
        Check.NotNull(query);

        var (deleterId, deletionTime) = ResolveActor<T>(query);
        return query.BatchSoftDeleteByAsync(deleterId, deletionTime, cancellationToken);
    }

    /// <summary>
    /// 批量软删除（IQueryable 扩展），删除人与时间由调用方给出（后台作业、迁移脚本，以及 Where 之后的组合查询：
    /// EF Core 没有公开途径从组合查询拿到 DbContext，自动解析只对查询根成立）。
    /// </summary>
    /// <remarks>
    /// 刻意与 <c>BatchSoftDeleteAsync</c> 分开命名：两者都带可选的 <c>cancellationToken</c>，同名重载会让日后给任一方
    /// 加可选参数变成二进制破坏（RS0026），与 <c>DataScopeExtensions.CanAccessByIdAsync</c> 同一处理。
    /// </remarks>
    /// <typeparam name="T">实体类型（必须实现 ISoftDelete）</typeparam>
    /// <param name="query">查询对象</param>
    /// <param name="deleterId">删除人；无操作人时为 null</param>
    /// <param name="deletionTime">删除时间（UTC）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>受影响的行数</returns>
    public static Task<int> BatchSoftDeleteByAsync<T>(
        this IQueryable<T> query,
        Guid? deleterId,
        DateTime deletionTime,
        CancellationToken cancellationToken = default)
        where T : class, ISoftDelete
    {
        Check.NotNull(query);

        return query.ExecuteUpdateAsync(s => SoftDeleteSetters.Apply(s, deleterId, deletionTime), cancellationToken);
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
    /// 批量软删除符合条件的实体：与仓储 <c>DeleteAsync(predicate)</c> 产出逐列相同的软删行。
    /// 删除人与时间在组合 <paramref name="predicate"/> 之前从 DbSet 根解析；解析不到而实体需要时抛出，
    /// 请改用 <c>BatchSoftDeleteByAsync(deleterId, deletionTime)</c>。
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
        Check.NotNull(dbSet);
        Check.NotNull(predicate);

        // 在组合 Where 之前从 DbSet 根解析：组合后的查询拿不到 DbContext
        var (deleterId, deletionTime) = ResolveActor<T>(dbSet);
        return dbSet.Where(predicate).BatchSoftDeleteByAsync(deleterId, deletionTime, cancellationToken);
    }

    #endregion

    /// <summary>
    /// 从查询所属 DbContext 的应用容器解析删除人与删除时间。
    /// </summary>
    /// <remarks>
    /// 实体带删除人 / 修改人字段而 <see cref="ICurrentUser"/> 解析不到时抛出：拿不到操作人却照样软删，
    /// 得到的是一批与历史脏数据无法区分的行。<see cref="TimeProvider"/> 缺席退回系统时钟，
    /// 那是合法默认值不是降级。
    /// </remarks>
    private static (Guid? DeleterId, DateTime DeletionTime) ResolveActor<T>(IQueryable<T> query) where T : class
    {
        // 只有查询根（DbSet 本身 / 仓储 AsQueryable(withTracking: true)）暴露 DbContext；AsNoTracking() 或 Where 之后的组合查询在 EF Core 里没有公开途径拿到它。
        var dbContext = query is IInfrastructure<IServiceProvider> accessor
            ? accessor.Instance.GetService<ICurrentDbContext>()?.Context
            : null;
        var serviceProvider = dbContext != null ? DbContextServiceResolver.GetServiceProvider(dbContext) : null;
        var currentUser = serviceProvider?.GetService<ICurrentUser>();

        if (currentUser == null && SoftDeleteSetters.RequiresActor(typeof(T)))
        {
            throw new InvalidOperationException(
                $"BatchSoftDeleteAsync cannot resolve ICurrentUser for entity {typeof(T).Name}, which records the deleter. " +
                "Call it on the DbSet with a predicate (dbContext.Set<T>().BatchSoftDeleteAsync(predicate)) so the DbContext " +
                "and its application container are reachable, or use BatchSoftDeleteByAsync(deleterId, deletionTime) on the composed query.");
        }

        var timeProvider = serviceProvider?.GetService<TimeProvider>() ?? TimeProvider.System;
        return (currentUser?.Id, timeProvider.GetUtcNow().UtcDateTime);
    }
}