namespace Tnzi.Storage.Services;

/// <summary>
/// 孤立引用清理每一轮从哪里接着查：按租户记一个 id 游标。进程内单例，由存储模块注册。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>它挡住的是「开关开着却什么都没查」。</b>孤立引用清理每轮只查 <c>MaxFilesPerRun</c> 条，
/// 而<b>实体还在的引用查完不会被删</b>。没有游标时每轮不带排序地取前 N 条，取到的是同一批有效引用：
/// 一个引用多于 N 条的库里，排在后面的真孤立引用永远轮不到，清理每次报 0。
/// 同模块的批量完整性校验与缩略图回填为同一个形态改成过 id 游标，这一趟当时没有跟上。
/// </para>
/// <para>
/// ★ <b>游标只活在进程里</b>（不加列、不加表，也就没有迁移）。代价是重启后丢失位置，所以重启后的第一轮
/// 从一个<b>随机位置</b>开始而不是从头开始：重启比扫完一整圈更频繁的部署，从头开始会让尾部永远轮不到，
/// 那与没有游标是同一个形态。扫到末尾之后回到开头。多实例各有各的游标，互不妨碍，只是各自推进。
/// </para>
/// </remarks>
public sealed class OrphanReferenceScanCursor
{
    private readonly ConcurrentDictionary<Guid, Position> _positions = new();

    /// <summary>一个租户的扫描位置：<c>After</c> 为空表示从头开始。</summary>
    internal readonly record struct Position(Guid? After);

    /// <summary>取这个租户的扫描位置；本进程里还没扫过它时返回 <see langword="false"/>。</summary>
    internal bool TryGet(Guid? tenantId, out Position position)
        => _positions.TryGetValue(Key(tenantId), out position);

    /// <summary>记下这个租户下一轮从哪里开始。</summary>
    internal void Set(Guid? tenantId, Position position)
        => _positions[Key(tenantId)] = position;

    private static Guid Key(Guid? tenantId) => tenantId ?? Guid.Empty;
}
