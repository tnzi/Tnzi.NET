namespace Tnzi.Storage.Services;

/// <summary>
/// <see cref="FileRecord.ReferenceCount"/> 的唯一写法：原子 SQL 增减，以及「无人持有才删」的条件删除。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>为什么不能「读出来 ++ 再写回」</b>：<see cref="FileRecord"/> 没有并发令牌。两个请求同时绑定同一份文件
/// （或同一个人并发正式上传同一份字节、命中同一条 MD5 记录），都读到 1、都写回 2，实际持有者却是 3 个；
/// 之后两次正常释放就归零，第三个持有者引用的文件被物理删除。改名、改可见性、挪目录这类整行写回同样会把
/// 别人刚加上的计数覆盖回旧值。现在计数只在数据库里加减（<c>SET ReferenceCount = ReferenceCount + 1</c>），
/// 而模型把这一列配成更新时不写（<c>FileRecordConfiguration</c>），整行写回再也碰不到它。
/// </para>
/// <para>
/// ★ <b>条件删除</b>：清理与删除是「先查出候选、再逐条删」，两步之间别的请求可能刚把它重新持有
/// （MD5 复用命中、实体绑定）。删除改成一条带判据的 <c>DELETE … WHERE</c>，判据在数据库里求值，
/// 删没删掉看受影响行数；没删掉就什么都不做，对象也不碰。
/// </para>
/// <para>
/// 裸 SQL 不经 SaveChanges：调用方开着工作单元时先让物理事务开启，这些语句才会加入它（回滚时一并撤销，
/// 行锁持有到提交）。没有外层事务时每条语句各自提交，与此前「不保证回滚」的约定一致。
/// </para>
/// <para>
/// <b>变更跟踪器里的那一份</b>：原子更新之后，本作用域里已被跟踪的实例按库里的新值对齐（当前值与原始值一起改，
/// 不会因此变成待更新），读它的代码看到的是真实计数。行还没落库的记录（同一个工作单元里刚插入、尚未 flush，
/// 例如上传与绑定在同一个事务里）原子更新找不到它：改它待插入的那份，计数随插入写下去
/// （插入照常写这一列，只有更新不写）。被跟踪、却已不在库里的记录（期间被别的请求删掉）答「不存在」，
/// 调用方不会拿着一条已删除的记录当它还在。
/// </para>
/// </remarks>
internal static class FileReferenceCounts
{
    /// <summary>
    /// 计数 +1。返回加完之后的计数；记录不存在返回 <see langword="null"/>。
    /// </summary>
    public static Task<int?> AddAsync(IRepository<FileRecord, Guid> files, Guid fileId, CancellationToken cancellationToken = default)
        => ApplyAsync(files, fileId,
            s => s.SetProperty(f => f.ReferenceCount, f => f.ReferenceCount + 1),
            pending => Math.Max(0, pending) + 1,
            cancellationToken);

    /// <summary>
    /// 计数 -1，不低于 0。返回减完之后的计数；记录不存在返回 <see langword="null"/>。
    /// </summary>
    public static Task<int?> ReleaseAsync(IRepository<FileRecord, Guid> files, Guid fileId, CancellationToken cancellationToken = default)
        => ApplyAsync(files, fileId,
            s => s.SetProperty(f => f.ReferenceCount, f => f.ReferenceCount > 0 ? f.ReferenceCount - 1 : 0),
            pending => Math.Max(0, pending - 1),
            cancellationToken);

    /// <summary>
    /// 把计数写成一个确定的值（按引用行重算时用）。返回写下的值；记录不存在返回 <see langword="null"/>。
    /// </summary>
    public static Task<int?> SetAsync(IRepository<FileRecord, Guid> files, Guid fileId, int count, CancellationToken cancellationToken = default)
        => ApplyAsync(files, fileId,
            s => s.SetProperty(f => f.ReferenceCount, count),
            _ => count,
            cancellationToken);

    /// <summary>
    /// 临时记录转正：<c>IsTemporary → false</c>、计数 +1（新增的隐式持有者，计数为负时从 0 起算），同一条语句，
    /// 只在它此刻仍是临时记录时生效。返回是否由这一次调用转正的：两次并发的转正只有一次加计数。
    /// </summary>
    public static async Task<bool> TryPromoteAsync(IRepository<FileRecord, Guid> files, Guid fileId, CancellationToken cancellationToken = default)
    {
        Check.NotNull(files);

        await files.EnsureTransactionStartedAsync(cancellationToken);
        var promoted = await files.AsQueryable()
            .Where(f => f.Id == fileId && f.IsTemporary)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(f => f.IsTemporary, false)
                    .SetProperty(f => f.ReferenceCount, f => (f.ReferenceCount > 0 ? f.ReferenceCount : 0) + 1),
                cancellationToken);
        if (promoted == 0)
            return false;

        await SyncTrackedCountAsync(files, fileId, FindTracked(files, fileId), cancellationToken);
        return true;
    }

    /// <summary>
    /// 给一条正式记录添一个<b>隐式持有者</b>，前提是它此刻一个都没有（计数不超过正式引用行数），同一条语句。
    /// 有了就什么都不改。返回是否添了。临时记录不归这里管（它们走 <see cref="TryPromoteAsync"/>）。
    /// </summary>
    /// <remarks>
    /// 用在「把文件放进文件空间」上：只剩业务记录在持有的正式文件（上传者删掉了自己那一份、而一条消息还引用着它）
    /// 计数恰好等于引用行数，没有隐式持有者。它被归档进用户的目录之后，文件空间成了它的又一个持有者 ——
    /// 不添这一份，引用它的消息一删，计数归零，文件被物理删除，而它还躺在用户的目录里。已经有隐式持有者的不再加，否则每挪一次目录删除时就要多删一次。
    /// 计数低于引用行数（丢过一次递增）时按引用行数重新起算，再加这一份。
    /// </remarks>
    public static async Task<bool> TryTakeImplicitHoldAsync(
        IRepository<FileRecord, Guid> files,
        IRepository<FileReference, Guid> references,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(files);
        Check.NotNull(references);

        await files.EnsureTransactionStartedAsync(cancellationToken);
        var referenceRows = references.AsQueryable();
        var taken = await files.AsQueryable()
            .Where(f => f.Id == fileId
                && !f.IsTemporary
                && f.ReferenceCount <= referenceRows.Count(r => r.FileId == f.Id && !r.IsTemporary))
            .ExecuteUpdateAsync(
                s => s.SetProperty(
                    f => f.ReferenceCount,
                    f => referenceRows.Count(r => r.FileId == f.Id && !r.IsTemporary) + 1),
                cancellationToken);
        if (taken == 0)
            return false;

        await SyncTrackedCountAsync(files, fileId, FindTracked(files, fileId), cancellationToken);
        return true;
    }

    /// <summary>
    /// 放掉一条记录的<b>全部</b>隐式持有者：计数写成它的正式引用行数，同一条语句，只在计数比引用行数多时生效。
    /// 返回是否改了。之后计数为 0 的记录由调用方条件删除。
    /// </summary>
    /// <remarks>
    /// 用在「从文件空间永久删除」上：用户删掉的是这个文件，不是这个文件的某一份持有。一条记录可以有不止一个隐式持有者
    /// （同一个人把同一份字节正式上传了两次，MD5 去重把两次记在同一条记录上），只放掉一个，它会带着剩下那一个
    /// 回到文件空间的根目录。业务记录的那几份（引用行）一份都不碰。
    /// </remarks>
    public static async Task<bool> ReleaseImplicitHoldsAsync(
        IRepository<FileRecord, Guid> files,
        IRepository<FileReference, Guid> references,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(files);
        Check.NotNull(references);

        await files.EnsureTransactionStartedAsync(cancellationToken);
        var referenceRows = references.AsQueryable();
        var released = await files.AsQueryable()
            .Where(f => f.Id == fileId
                && f.ReferenceCount > referenceRows.Count(r => r.FileId == f.Id && !r.IsTemporary))
            .ExecuteUpdateAsync(
                s => s.SetProperty(
                    f => f.ReferenceCount,
                    f => referenceRows.Count(r => r.FileId == f.Id && !r.IsTemporary)),
                cancellationToken);
        if (released == 0)
            return false;

        await SyncTrackedCountAsync(files, fileId, FindTracked(files, fileId), cancellationToken);
        return true;
    }

    /// <summary>
    /// 一条记录此刻是否无人持有：计数 &lt;= 0、没有正式引用行（计数与引用行对不上时不按计数删）、
    /// 不是归档进目录的存量临时记录（与孤儿回收同一条豁免）。成立就在同一条语句里删掉它，返回是否删掉了。
    /// </summary>
    /// <param name="files">文件记录仓储。</param>
    /// <param name="references">引用行仓储。</param>
    /// <param name="fileId">文件 id。</param>
    /// <param name="exemptFiledTemporaries">
    /// 归档进目录的存量临时记录是否豁免。自动回收要豁免（默认）；用户从文件空间永久删除时不豁免 —— 那条豁免是给自动回收的，
    /// 不拦人自己的删除。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 删掉之后，剩下的临时引用行（手动引用 API 的草稿，不计入计数）一并删掉；变更跟踪器里若还挂着这一行，
    /// 摘掉它，免得同一作用域里下一次 SaveChanges 去更新一行已经不存在的记录。对象不在这里删：
    /// 调用方在删掉之后再删对象（或发删除事件），没删掉就一个字节都不碰。
    /// </remarks>
    public static async Task<bool> TryDeleteUnheldAsync(
        IRepository<FileRecord, Guid> files,
        IRepository<FileReference, Guid> references,
        Guid fileId,
        CancellationToken cancellationToken = default,
        bool exemptFiledTemporaries = true)
    {
        Check.NotNull(files);
        Check.NotNull(references);

        await files.EnsureTransactionStartedAsync(cancellationToken);
        var referenceRows = references.AsQueryable();
        var deleted = await files.AsQueryable()
            .Where(f => f.Id == fileId
                && f.ReferenceCount <= 0
                && !(exemptFiledTemporaries && f.IsTemporary && f.FolderId != null)
                && !referenceRows.Any(r => r.FileId == f.Id && !r.IsTemporary))
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
            return false;

        await references.DeleteAsync(r => r.FileId == fileId, cancellationToken);
        DiscardTracked(files, fileId);
        return true;
    }

    /// <summary>
    /// 用户显式删除时的条件删除：只要求计数 &lt;= 0（期间被重新持有就不删），不看目录豁免，
    /// 那条豁免是给自动回收的，不拦人自己的删除。返回是否删掉了。
    /// </summary>
    /// <remarks>
    /// 同一个工作单元里刚插入、还没落库的记录（上传之后在同一个事务里又删掉）库里找不到：
    /// 把待插入的那份摘掉，它就不会在提交时被插进去、指着一个已经删掉的对象。
    /// </remarks>
    public static async Task<bool> TryDeleteReleasedAsync(IRepository<FileRecord, Guid> files, Guid fileId, CancellationToken cancellationToken = default)
    {
        Check.NotNull(files);

        await files.EnsureTransactionStartedAsync(cancellationToken);
        var deleted = await files.AsQueryable()
            .Where(f => f.Id == fileId && f.ReferenceCount <= 0)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted == 0)
        {
            var pending = FindTracked(files, fileId);
            if (pending is not { State: EntityState.Added } || pending.Entity.ReferenceCount > 0)
                return false;

            files.Discard(pending.Entity);
            return true;
        }

        DiscardTracked(files, fileId);
        return true;
    }

    private static async Task<int?> ApplyAsync(
        IRepository<FileRecord, Guid> files,
        Guid fileId,
        Action<UpdateSettersBuilder<FileRecord>> setter,
        Func<int, int> applyToPending,
        CancellationToken cancellationToken)
    {
        Check.NotNull(files);

        await files.EnsureTransactionStartedAsync(cancellationToken);
        var updated = await files.AsQueryable()
            .Where(f => f.Id == fileId)
            .ExecuteUpdateAsync(setter, cancellationToken);

        var tracked = FindTracked(files, fileId);
        if (updated == 0)
        {
            if (tracked is not { State: EntityState.Added })
                return null;

            tracked.Entity.ReferenceCount = applyToPending(tracked.Entity.ReferenceCount);
            return tracked.Entity.ReferenceCount;
        }

        return await SyncTrackedCountAsync(files, fileId, tracked, cancellationToken);
    }

    /// <summary>
    /// 读回库里的计数，并让本作用域里被跟踪的那一份对齐它（当前值与原始值一起改，不因此变成待更新）。
    /// </summary>
    private static async Task<int?> SyncTrackedCountAsync(
        IRepository<FileRecord, Guid> files,
        Guid fileId,
        EntityEntry<FileRecord>? tracked,
        CancellationToken cancellationToken)
    {
        var count = await files.AsQueryable()
            .Where(f => f.Id == fileId)
            .Select(f => (int?)f.ReferenceCount)
            .FirstOrDefaultAsync(cancellationToken);

        if (count is { } current && tracked != null)
        {
            var property = tracked.Property(f => f.ReferenceCount);
            property.OriginalValue = current;
            property.CurrentValue = current;
            property.IsModified = false;
        }

        return count;
    }

    /// <summary>
    /// 本作用域里已被跟踪的那一份（没有则 null）。仓储的跟踪查询根就是 DbSet，经它拿到 DbContext；
    /// 拿不到（非 EF 的仓储实现）时当作没有被跟踪。
    /// </summary>
    /// <remarks>
    /// 按主键在身份映射里查（O(1)），查的时候关掉自动变更检测：访问 <c>Local</c> 默认先对整个变更跟踪器跑一遍
    /// DetectChanges，一次保存绑定上千个文件时那是平方级的开销，而这里只要主键与状态。
    /// </remarks>
    private static EntityEntry<FileRecord>? FindTracked(IRepository<FileRecord, Guid> files, Guid fileId)
    {
        var context = files.AsQueryable(withTracking: true) is IInfrastructure<IServiceProvider> accessor
            ? accessor.Instance.GetService<ICurrentDbContext>()?.Context
            : null;
        if (context == null)
            return null;

        var tracker = context.ChangeTracker;
        var autoDetectChanges = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;
        try
        {
            return context.Set<FileRecord>().Local.FindEntry(fileId);
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }

    private static void DiscardTracked(IRepository<FileRecord, Guid> files, Guid fileId)
    {
        var tracked = FindTracked(files, fileId);
        if (tracked != null)
            files.Discard(tracked.Entity);
    }
}
