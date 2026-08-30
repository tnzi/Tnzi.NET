namespace Tnzi.Domain.Repositories;

/// <summary>
/// 仓储的排序重排扩展：把 <see cref="SortOrderPlanner"/> 的槽位保留规划接到持久化上。
/// </summary>
/// <remarks>
/// <para>这是各业务模块 <c>reorder</c> 端点的统一落点——没有它的时候，每个模块要么各写一遍
/// （已出现两套口径不同的实现），要么干脆只能一条条改序号。</para>
/// <para><b>范围（scope）必须显式给出。</b>树形或分组的实体（同一父下排序、同一分组内排序）
/// 若不传 <c>scope</c>，全部记录会被当成一条序列重排——那不是「排序错了」，
/// 而是把别的父节点下的记录也一起挤动了。无分组的实体传 <c>null</c> 表示全局唯一序列。</para>
/// </remarks>
[ExperimentalApi(Reason = "Reordering primitive is still being shaped by its first consumers; the signature may change before 1.0")]
public static class RepositoryReorderExtensions
{
    /// <summary>
    /// 按提交的顺序重排指定范围内的记录（槽位保留，不挤动范围外的任何记录）。
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <typeparam name="TKey">主键类型</typeparam>
    /// <param name="repository">目标仓储</param>
    /// <param name="orderedIds">用户提交的新顺序（通常是当前页的可见顺序）</param>
    /// <param name="scope">重排范围谓词；<c>null</c> = 该实体是一条全局序列</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功时携带实际写库的条数（顺序没变则为 0）</returns>
    public static async Task<Result<int>> ReorderAsync<TEntity, TKey>(
        this IRepository<TEntity, TKey> repository,
        IReadOnlyList<TKey> orderedIds,
        Expression<Func<TEntity, bool>>? scope = null,
        CancellationToken cancellationToken = default)
        where TEntity : class, IEntity<TKey>, IHasOrder
        where TKey : notnull
    {
        Check.NotNull(repository);
        Check.NotNull(orderedIds);

        // 范围内全量读出后在内存里定序。重排本来就必须看到整段序列（这正是槽位保留的前提），
        // 所以这里没有「只读一页」的省法；而定序放在内存是因为核心层只依赖仓储抽象，
        // 拿不到 EF 的 OrderBy 翻译，且同一 scope 的记录量本就受限于「人能拖得动」。
        var current = await repository.ToListAsync(scope, cancellationToken);
        var sorted = current
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.Id, Comparer<TKey>.Default)
            .ToList();

        var plan = SortOrderPlanner.Plan(sorted, orderedIds, e => e.Id);
        if (!plan.IsValid)
            return Result.Failure<int>(plan.Error!, plan.ErrorStatusCode);

        var changed = plan.Apply();
        if (changed.Count == 0)
            return Result<int>.Success(0);

        await repository.UpdateManyAsync(changed, cancellationToken);
        return Result<int>.Success(changed.Count);
    }
}
