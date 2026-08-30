namespace Tnzi.Domain.Entities;

/// <summary>
/// <see cref="SortOrderPlanner.Plan{TEntity,TKey}"/> 的规划结果。
/// </summary>
/// <remarks>
/// <para>规划阶段<b>不写实体</b>：<see cref="Ordered"/> 只是新顺序的视图，
/// <see cref="Changes"/> 是「序号真的变了」的那些赋值。调用 <see cref="Apply"/> 才落到实体上。
/// 拆成两步是为了让规划本身可以被无副作用地测试与断言，
/// 也让调用方能在写库之前先看一眼「这次到底会动几条」。</para>
/// </remarks>
/// <typeparam name="TEntity">实体类型</typeparam>
public sealed class SortOrderPlan<TEntity>
    where TEntity : IHasOrder
{
    private SortOrderPlan(
        bool isValid,
        string? error,
        int errorStatusCode,
        IReadOnlyList<TEntity> ordered,
        IReadOnlyList<(TEntity Entity, int SortOrder)> changes)
    {
        IsValid = isValid;
        Error = error;
        ErrorStatusCode = errorStatusCode;
        Ordered = ordered;
        Changes = changes;
    }

    /// <summary>规划是否成立。为 <c>false</c> 时 <see cref="Error"/> 说明原因。</summary>
    public bool IsValid { get; }

    /// <summary>失败原因（面向 API 使用者的英文消息）；成立时为 <c>null</c>。</summary>
    public string? Error { get; }

    /// <summary>失败时建议的 HTTP 状态码（400 请求本身有问题 / 404 记录已不在范围内）；成立时为 0。</summary>
    public int ErrorStatusCode { get; }

    /// <summary>重排后的<b>全量</b>顺序（含本次未提交、原地不动的记录）。失败时为空。</summary>
    public IReadOnlyList<TEntity> Ordered { get; }

    /// <summary>序号确实发生变化、需要写库的赋值。失败时为空。</summary>
    public IReadOnlyList<(TEntity Entity, int SortOrder)> Changes { get; }

    /// <summary>
    /// 把 <see cref="Changes"/> 写进实体的 <see cref="IHasOrder.SortOrder"/>。
    /// </summary>
    /// <returns>被改动的实体（顺序与 <see cref="Changes"/> 一致），可直接交给 <c>UpdateManyAsync</c></returns>
    public IReadOnlyList<TEntity> Apply()
    {
        var changed = new List<TEntity>(Changes.Count);
        foreach (var (entity, sortOrder) in Changes)
        {
            entity.SortOrder = sortOrder;
            changed.Add(entity);
        }

        return changed;
    }

    internal static SortOrderPlan<TEntity> Invalid(string error, int statusCode) =>
        new(false, error, statusCode, [], []);

    internal static SortOrderPlan<TEntity> Valid(
        IReadOnlyList<TEntity> ordered,
        IReadOnlyList<(TEntity Entity, int SortOrder)> changes) =>
        new(true, null, 0, ordered, changes);
}

/// <summary>
/// 排序重排规划器：把「用户拖出来的一段顺序」安全地并回一个已排好序的全量序列。
/// </summary>
/// <remarks>
/// <para><b>为什么不能直接给提交的这几条编 1..N。</b>界面上能拖动的通常只有当前这一页
/// （或某个筛选后的子集）。若把提交的 N 条编成 1..N，它们就会与页外记录的既有序号相撞：
/// 打平之后谁先谁后由数据库的次级排序（创建时间、主键）决定，于是一条记录悄无声息地
/// 抢走另一条的位置，而操作员看到的是自己刚拖出来的顺序——两边都「没报错」。</para>
/// <para><b>槽位保留（slot preserving）。</b>本规划器先取出提交集合<b>当前占据的那些位置</b>，
/// 再把提交顺序按位填回去；没提交的记录一律留在原来的位置上。因此拖动只在可见范围内
/// 重新分配位置，永远不会挤动范围之外的任何记录。跨页移动做不到（那需要一次提交全量顺序），
/// 但换来的是「不会撞号」这个更重要的保证。</para>
/// <para>本类是<b>纯内存计算</b>（不做 I/O、不写实体），持久化与写回由调用方决定。
/// 仓储层的现成接线见 <c>IRepository&lt;TEntity,TKey&gt;.ReorderAsync</c>。</para>
/// </remarks>
[ExperimentalApi(Reason = "Reordering primitive is still being shaped by its first consumers; the signature may change before 1.0")]
public static class SortOrderPlanner
{
    /// <summary>
    /// 在全量序列上按「槽位保留」并入提交的顺序。
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <typeparam name="TKey">实体主键类型</typeparam>
    /// <param name="currentOrder">重排范围内的<b>全量</b>记录，须已按当前 <see cref="IHasOrder.SortOrder"/> 排好且顺序确定</param>
    /// <param name="requestedIds">用户提交的新顺序（通常是当前页），必须是 <paramref name="currentOrder"/> 的子集且不含重复</param>
    /// <param name="keySelector">主键选择器</param>
    /// <param name="startAt">首位的序号，默认 1</param>
    /// <returns>规划结果；不成立时携带原因与建议状态码，调用方不应写库</returns>
    public static SortOrderPlan<TEntity> Plan<TEntity, TKey>(
        IReadOnlyList<TEntity> currentOrder,
        IReadOnlyList<TKey> requestedIds,
        Func<TEntity, TKey> keySelector,
        int startAt = 1)
        where TEntity : IHasOrder
        where TKey : notnull
    {
        Check.NotNull(currentOrder);
        Check.NotNull(requestedIds);
        Check.NotNull(keySelector);

        if (requestedIds.Count == 0)
            return SortOrderPlan<TEntity>.Invalid("At least one record id is required.", 400);

        var comparer = EqualityComparer<TKey>.Default;
        var submitted = new HashSet<TKey>(comparer);
        foreach (var id in requestedIds)
        {
            if (!submitted.Add(id))
                return SortOrderPlan<TEntity>.Invalid(
                    "The same record appears more than once in the requested order.", 400);
        }

        var byId = new Dictionary<TKey, TEntity>(currentOrder.Count, comparer);
        foreach (var entity in currentOrder)
        {
            // 同一主键出现两次说明 currentOrder 不是一个合法的序列（调用方拼错了范围，
            // 或查询做了会产生重复行的 join）。继续算下去会让槽位数与提交数对不上，
            // 那时的症状是「拖了没反应」或顺序错乱——在这里直接说清楚。
            if (!byId.TryAdd(keySelector(entity), entity))
                return SortOrderPlan<TEntity>.Invalid(
                    "The reordered group contains duplicate records.", 400);
        }

        foreach (var id in requestedIds)
        {
            if (!byId.ContainsKey(id))
                return SortOrderPlan<TEntity>.Invalid(
                    "One or more records no longer exist, or fall outside the group being reordered. Reload and retry.",
                    404);
        }

        // 提交集合当前占据的那些位置，按提交顺序填回去；没提交的记录原地不动。
        var ordered = new List<TEntity>(currentOrder);
        var slot = 0;
        for (var i = 0; i < currentOrder.Count; i++)
        {
            if (!submitted.Contains(keySelector(currentOrder[i])))
                continue;

            ordered[i] = byId[requestedIds[slot]];
            slot++;
        }

        var changes = new List<(TEntity Entity, int SortOrder)>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var target = startAt + i;
            if (ordered[i].SortOrder == target)
                continue;

            changes.Add((ordered[i], target));
        }

        return SortOrderPlan<TEntity>.Valid(ordered, changes);
    }
}
