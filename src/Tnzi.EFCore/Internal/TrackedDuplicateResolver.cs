
namespace Tnzi.EFCore.Internal;

/// <summary>
/// 处理"传入的实例与变更跟踪器里已有的条目同主键"这一情形。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>为什么需要它</b>：框架里最常见的写法是「只读查询读出来 → 改 → <c>UpdateAsync</c>」，
/// 而 <c>FindAsync(predicate)</c> / <c>FirstOrDefaultAsync</c> 一类只读查询一律走
/// <c>AsNoTracking</c>，拿到的必然是<b>脱离跟踪的新实例</b>。同一个作用域内第二次读到同一行
/// （重试、批量里出现重复项、或者这一行本来就是刚刚在这个作用域里写进去的），
/// <c>Attach</c> / <c>UpdateRange</c> 就会撞上已跟踪的那一条并抛
/// <c>InvalidOperationException: another instance with the same key value is already being tracked</c>。
/// </para>
/// <para>
/// 调用方<b>无从预防</b>：<c>IRepository</c> 不暴露变更跟踪器，也没有任何签名或文档说过
/// 「同一个作用域里同一行只能写一次」。所以收口只能在仓储。
/// </para>
/// </remarks>
internal static class TrackedDuplicateResolver
{
    /// <summary>
    /// 在变更跟踪器里查找与 <paramref name="entity"/> 主键相同、但不是同一个实例的条目；没有则返回 null。
    /// </summary>
    /// <remarks>
    /// 只在传入实体处于 <see cref="EntityState.Detached"/> 时调用。主键未赋值
    /// （<c>IsKeySet == false</c>）直接返回 null：那是一条尚未定型的新行，
    /// 与已跟踪的行合并只会把两条不相干的记录并成一条。
    /// <para>
    /// ★ <b>刻意不用 <c>Local.FindEntry</c></b>（身份映射 O(1) 查找）：它只有
    /// <c>FindEntry&lt;TKey&gt;(TKey keyValue)</c> 一个单值主键重载，没有接收多个键值的版本，
    /// 传数组进去会被当成「主键类型是 object[]」在运行时抛 <c>ArgumentException</c>，
    /// 复合主键实体因此走不通。这里改为扫本类型的已跟踪条目：代价是 O(本类型已跟踪条目数)，
    /// 而「传入实体是 Detached」与「本类型已跟踪大量条目」这两个条件基本互斥
    /// （后者意味着调用方用的是带跟踪查询，那样拿到的实体就不是 Detached 了）。
    /// </para>
    /// <para>
    /// 扫描期间关闭 <c>AutoDetectChangesEnabled</c>：这里只读主键值，不需要变更检测，
    /// 而 <c>Entries&lt;T&gt;()</c> 默认会先对整个对象图跑一遍 DetectChanges。
    /// </para>
    /// </remarks>
    public static EntityEntry<TEntity>? Find<TEntity>(DbContext dbContext, TEntity entity)
        where TEntity : class
    {
        var detached = dbContext.Entry(entity);
        if (!detached.IsKeySet)
        {
            return null;
        }

        var keyProperties = dbContext.Model.FindEntityType(typeof(TEntity))?.FindPrimaryKey()?.Properties;
        if (keyProperties == null || keyProperties.Count == 0)
        {
            return null;
        }

        var keyValues = new object?[keyProperties.Count];
        for (var i = 0; i < keyValues.Length; i++)
        {
            keyValues[i] = detached.Property(keyProperties[i].Name).CurrentValue;
        }

        var changeTracker = dbContext.ChangeTracker;
        var autoDetectChanges = changeTracker.AutoDetectChangesEnabled;
        changeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var candidate in changeTracker.Entries<TEntity>())
            {
                if (ReferenceEquals(candidate.Entity, entity) || candidate.State == EntityState.Detached)
                {
                    continue;
                }

                var matched = true;
                for (var i = 0; i < keyValues.Length && matched; i++)
                {
                    matched = Equals(candidate.Property(keyProperties[i].Name).CurrentValue, keyValues[i]);
                }

                if (matched)
                {
                    return candidate;
                }
            }
        }
        finally
        {
            changeTracker.AutoDetectChangesEnabled = autoDetectChanges;
        }

        return null;
    }

    /// <summary>
    /// 判断 <paramref name="tracked"/> 能否接管这次更新。
    /// </summary>
    /// <remarks>
    /// Deleted 例外：合并等于悄悄复活一条已标记删除的行，跳过等于悄悄丢掉这次更新，
    /// 两种都是静默的错误语义。这条分支本来就不抛（实测 EF 的 <c>Attach</c> 会把 Deleted 条目
    /// 顶掉、待删的行改成被更新），所以刻意<b>不接管</b>它 —— 删除语义不在这次收口范围内，
    /// 换一种静默行为只会把问题挪个地方。
    /// </remarks>
    public static bool CanMergeInto<TEntity>(EntityEntry<TEntity>? tracked)
        where TEntity : class
        => tracked != null && tracked.State != EntityState.Deleted;

    /// <summary>
    /// 把脱离跟踪的实例上的值合并到已跟踪的同主键条目上。
    /// </summary>
    /// <remarks>
    /// 已跟踪的那一条是权威实例（别处可能正持有它的引用），所以合并方向只能是
    /// detached → tracked，绝不反过来 Detach 已跟踪的实例。
    /// </remarks>
    public static void Merge<TEntity>(EntityEntry<TEntity> tracked, TEntity incoming)
        where TEntity : class
    {
        tracked.CurrentValues.SetValues(incoming);

        // Added 保持 Added（理由同 EFCoreRepository.UpdateAsync：INSERT 不能被降级成 UPDATE）。
        if (tracked.State != EntityState.Added)
        {
            tracked.State = EntityState.Modified;
        }
    }
}
