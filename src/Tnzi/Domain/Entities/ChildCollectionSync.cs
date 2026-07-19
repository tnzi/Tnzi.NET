namespace Tnzi.Domain.Entities;

/// <summary>
/// <see cref="ChildCollectionSync.ReplaceChildren{TParent,TChild,TKey}"/> 的差异统计结果。
/// </summary>
/// <param name="Added">新增（Insert）的子项数</param>
/// <param name="HardRemoved">从集合移除（物理删除）的子项数</param>
/// <param name="SoftDeleted">标记软删除（<see cref="ISoftDelete.IsDeleted"/>=true）的子项数</param>
/// <param name="Revived">复活（原为软删、目标集合再次包含 → <see cref="ISoftDelete.IsDeleted"/>=false）的子项数</param>
public readonly record struct ChildCollectionSyncResult(int Added, int HardRemoved, int SoftDeleted, int Revived);

/// <summary>
/// 子集合「整体替换」助手：按业务键 diff 父实体的已加载子集合与目标集合，得出增/删，
/// 被移除且实现 <see cref="ISoftDelete"/> 的子项标记软删（保留在集合里让 EF 更新为 <c>IsDeleted=true</c>），
/// 否则从集合移除（让 EF 物理删除）；目标里存在而集合里没有的子项加入集合（EF 插入）。
/// </summary>
/// <remarks>
/// <para>消除消费者「清空集合再手写逐条删除」的样板，以及「清空跟踪集合不软删=软删实体被物理 DELETE」的坑。</para>
/// <para>本方法是<b>纯内存集合操作</b>（不做 I/O）：持久化由外层工作单元的 <c>SaveChangesAsync</c> 完成，
/// 因此有意<b>非异步</b>，避免 async-over-sync。前置条件：<paramref name="parent"/> 的子集合已加载
/// （EF 导航属性），且父实体处于变更跟踪中。</para>
/// <para>键匹配语义：目标与现有子项按 <paramref name="keySelector"/> 的键比较——
/// 两侧都有=保留（若曾软删则复活）；仅现有=移除（软删或物理删）；仅目标=新增。
/// 新增子项的键为默认值（尚未持久化）时一律视为新增。<b>匹配到的子项其标量值不会被覆盖</b>
/// （整体替换只处理增删，字段更新请另行映射）。</para>
/// </remarks>
[StableApi(Since = "0.1.0")]
public static class ChildCollectionSync
{
    /// <summary>
    /// 用 <paramref name="newItems"/> 整体替换 <paramref name="parent"/> 的子集合。
    /// </summary>
    /// <typeparam name="TParent">父实体类型</typeparam>
    /// <typeparam name="TChild">子实体类型</typeparam>
    /// <typeparam name="TKey">子实体业务键类型</typeparam>
    /// <param name="parent">父实体（子集合须已加载、父实体须处于变更跟踪中）</param>
    /// <param name="childrenSelector">从父实体取出可变子集合</param>
    /// <param name="newItems">目标子集合</param>
    /// <param name="keySelector">子实体业务键选择器（用于 diff 匹配）</param>
    /// <returns>本次同步的增删统计</returns>
    public static ChildCollectionSyncResult ReplaceChildren<TParent, TChild, TKey>(
        TParent parent,
        Func<TParent, ICollection<TChild>> childrenSelector,
        IEnumerable<TChild> newItems,
        Func<TChild, TKey> keySelector)
        where TChild : class
    {
        Check.NotNull(parent);
        Check.NotNull(childrenSelector);
        Check.NotNull(newItems);
        Check.NotNull(keySelector);

        var children = childrenSelector(parent);
        Check.NotNull(children);

        var newList = newItems as IReadOnlyCollection<TChild> ?? newItems.ToList();
        var comparer = EqualityComparer<TKey>.Default;
        var targetKeys = new HashSet<TKey>(newList.Select(keySelector), comparer);

        var added = 0;
        var hardRemoved = 0;
        var softDeleted = 0;
        var revived = 0;

        // Pass 1: walk existing children — keep (revive if it was soft-deleted),
        // soft-delete, or hard-remove. Snapshot first so we can mutate the collection.
        foreach (var existing in children.ToList())
        {
            var key = keySelector(existing);
            if (targetKeys.Contains(key))
            {
                if (existing is ISoftDelete { IsDeleted: true } revivable)
                {
                    revivable.IsDeleted = false;
                    revived++;
                }
                continue;
            }

            if (existing is ISoftDelete softDeletable)
            {
                if (!softDeletable.IsDeleted)
                {
                    softDeletable.IsDeleted = true;
                    softDeleted++;
                }
            }
            else
            {
                children.Remove(existing);
                hardRemoved++;
            }
        }

        // Pass 2: add target items that are not already represented in the collection.
        var existingKeys = new HashSet<TKey>(children.Select(keySelector), comparer);
        foreach (var item in newList)
        {
            var key = keySelector(item);
            var isDefaultKey = comparer.Equals(key, default!);
            if (isDefaultKey || !existingKeys.Contains(key))
            {
                children.Add(item);
                added++;
                if (!isDefaultKey)
                    existingKeys.Add(key);
            }
        }

        return new ChildCollectionSyncResult(added, hardRemoved, softDeleted, revived);
    }
}
