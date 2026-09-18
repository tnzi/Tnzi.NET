namespace Tnzi.EFCore.Internal;

/// <summary>
/// 数据库端软删除（<c>ExecuteUpdateAsync</c>）的 SetProperty 链，按实体实现的接口组合动态拼装。
/// </summary>
/// <remarks>
/// 仓储 <c>DeleteAsync(predicate)</c> 与 <c>BatchOperationExtensions.BatchSoftDeleteAsync</c> 共用这一份：
/// 同一语义两条路径必须产出逐列相同的行。裸 SQL 绕过变更跟踪器，拦截器层的就地软删补写
/// （<see cref="AuditPropertyHelper"/>）在这里不会执行，所以删除人、删除时间、修改人、修改时间与
/// 并发戳都要在这条链里显式写；少写任何一列都是一批「没有人、在没有时间删掉的」软删行，
/// 且并发戳不变意味着持旧快照的写入仍能通过。
/// </remarks>
internal static class SoftDeleteSetters
{
    /// <summary>实体是否带有需要「操作人」的审计字段（删除人 / 修改人）。</summary>
    public static bool RequiresActor(Type entityType) =>
        typeof(IHasDeleter).IsAssignableFrom(entityType) || typeof(IHasModifier).IsAssignableFrom(entityType);

    /// <summary>
    /// 把软删除的全部赋值挂到 <paramref name="setters"/> 上。
    /// </summary>
    /// <typeparam name="TEntity">实体类型（须实现 <see cref="ISoftDelete"/>，其余接口按实现与否决定是否写入）</typeparam>
    /// <param name="setters">EF 的赋值构造器</param>
    /// <param name="deleterId">删除人；匿名或后台作业为 null</param>
    /// <param name="deletionTime">删除时间（UTC）</param>
    public static void Apply<TEntity>(UpdateSettersBuilder<TEntity> setters, Guid? deleterId, DateTime deletionTime) where TEntity : class
    {
        Check.NotNull(setters);

        var entityType = typeof(TEntity);
        var hasDeleter = typeof(IHasDeleter).IsAssignableFrom(entityType);
        var hasModTime = typeof(IHasModificationTime).IsAssignableFrom(entityType);
        var hasModifier = typeof(IHasModifier).IsAssignableFrom(entityType);
        var hasConcurrency = typeof(IConcurrencyStamp).IsAssignableFrom(entityType);

        setters.SetProperty(e => ((ISoftDelete)e).IsDeleted, true);

        if (hasDeleter)
        {
            setters.SetProperty(e => ((IHasDeleter)e).DeleterId, deleterId);
            setters.SetProperty(e => ((IHasDeleter)e).DeletionTime, deletionTime);
        }
        if (hasModTime)
        {
            setters.SetProperty(e => ((IHasModificationTime)e).LastModificationTime, (DateTime?)deletionTime);
        }
        if (hasModifier)
        {
            setters.SetProperty(e => ((IHasModifier)e).LastModifierId, deleterId);
        }
        if (hasConcurrency)
        {
            setters.SetProperty(e => ((IConcurrencyStamp)e).ConcurrencyStamp, Guid.NewGuid().ToString("N"));
        }
    }
}
