namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// 文件记录删除之后，收掉它在工作区里留下的东西：历史版本（行，以及每个版本自己的对象）与分享链接（行）。
/// </summary>
/// <remarks>
/// <para>
/// 父模块删除一条 <see cref="FileRecord"/> 时只管记录名下的原件与缩略图：它不认识本模块的表。而版本的对象只由版本行指向
/// （建过新版本之后，记录的 <c>Path</c> 已经换成新键，旧版本的字节只剩版本行认得），记录一走，读取与删除版本的端点
/// 都先查记录、一律 404，用户删掉的文件，它的每个旧版本却完整地永久留在存储里，没有任何途径再删除或读取它们，
/// 管理端的版本审计页仍列着这些行。
/// </para>
/// <para>
/// 两条入口：<see cref="FileRecordDeletedEvent"/>（删除提交之后立即执行）与后台清理的贡献者
/// <see cref="DeletedFileWorkspaceCleanupContributor"/>（兜底：事件重试用尽、或这条清理出现之前就已删除的文件）。
/// </para>
/// <para>
/// ★ 按文件 id 跨租户查询（<c>IgnoreQueryFilters</c>）：文件 id 全局唯一，记录在<b>任何</b>租户里都已不存在才动手。
/// 执行处的租户上下文不必与删除时一致，后台清理不在任何租户里，事件按捕获的租户恢复；两者都不该成为少删的理由，
/// 而「记录确实不存在」这一条保证了不会删到仍在用的数据。
/// </para>
/// <para>
/// ★ 对象先删、行后删。对象删不掉就抛出，行留着，下一次（事件重试、下一趟后台清理）还找得到它们；
/// 反过来先删行，删失败的对象就成了谁也找不到的孤儿。provider 的删除是幂等的，重做整件事是安全的。
/// 当前版本的对象与记录的 <c>Path</c> 是同一个，删除记录的那条路径已经删过一次，这里再删一次只是空操作。
/// </para>
/// </remarks>
public class DeletedFileWorkspaceCleaner
{
    private readonly IRepository<FileRecord, Guid> _files;
    private readonly IRepository<FileVersion, Guid> _versions;
    private readonly IRepository<FileShare, Guid> _shares;
    private readonly IRepository<RecycleBinEntry, Guid> _recycleBinEntries;
    private readonly IFileStorage _storage;
    private readonly ILogger<DeletedFileWorkspaceCleaner> _logger;

    public DeletedFileWorkspaceCleaner(
        IRepository<FileRecord, Guid> files,
        IRepository<FileVersion, Guid> versions,
        IRepository<FileShare, Guid> shares,
        IRepository<RecycleBinEntry, Guid> recycleBinEntries,
        IFileStorage storage,
        ILogger<DeletedFileWorkspaceCleaner> logger)
    {
        _files = Check.NotNull(files);
        _versions = Check.NotNull(versions);
        _shares = Check.NotNull(shares);
        _recycleBinEntries = Check.NotNull(recycleBinEntries);
        _storage = Check.NotNull(storage);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 收掉一个已删除文件的版本与分享。记录仍在时什么都不做。
    /// </summary>
    /// <returns>删掉的版本行与分享行合计条数；记录仍在或本来就没有残留时为 0。</returns>
    /// <exception cref="AggregateException">有版本对象删不掉；行保持原样，供下一次重做。</exception>
    public async Task<int> PurgeAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var recordExists = await _files.AsQueryable()
            .IgnoreQueryFilters()
            .AnyAsync(f => f.Id == fileId, cancellationToken);
        if (recordExists)
            return 0;

        var versionPaths = await _versions.AsQueryable()
            .IgnoreQueryFilters()
            .Where(v => v.FileId == fileId)
            .Select(v => v.Path)
            .Distinct()
            .ToListAsync(cancellationToken);

        List<Exception>? failures = null;
        foreach (var path in versionPaths.Where(p => !string.IsNullOrEmpty(p)))
        {
            try
            {
                // 内置 provider 遇到故障报 false 而不抛：只看有没有抛，瞬时故障就会被当成删掉了，
                // 随后连行一起删，对象从此谁也找不到。按「对象还在不在」判。
                if (!await StoredObjectDeletion.DeleteAndConfirmAsync(_storage, path))
                {
                    (failures ??= []).Add(new InvalidOperationException(
                        $"Stored version object '{path}' is still present after the delete request."));
                }
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is { Count: > 0 })
        {
            throw new AggregateException(
                $"Failed to delete {failures.Count} stored version object(s) of deleted file {fileId}; its version rows are kept so the deletion can be retried.",
                failures);
        }

        // 裸 SQL 不经 SaveChanges：调用方开着工作单元时先让物理事务开启，删除才会加入它。
        await _versions.EnsureTransactionStartedAsync(cancellationToken);
        var versionRows = await _versions.AsQueryable()
            .IgnoreQueryFilters()
            .Where(v => v.FileId == fileId)
            .ExecuteDeleteAsync(cancellationToken);
        var shareRows = await _shares.AsQueryable()
            .IgnoreQueryFilters()
            .Where(s => s.FileId == fileId)
            .ExecuteDeleteAsync(cancellationToken);
        // 文件在回收站里时被别的途径删掉了（父模块的 DELETE files/{id}、管理员批量删除）：条目指着一个不存在的文件。
        var recycleBinRows = await _recycleBinEntries.AsQueryable()
            .IgnoreQueryFilters()
            .Where(e => e.ItemType == RecycleBinItemType.File && e.ItemId == fileId)
            .ExecuteDeleteAsync(cancellationToken);

        if (versionRows + shareRows + recycleBinRows > 0)
        {
            _logger.LogInformation(
                "Removed {Versions} version(s), {Shares} share link(s) and {RecycleBin} recycle bin entry of deleted file {FileId}",
                versionRows, shareRows, recycleBinRows, fileId);
        }

        return versionRows + shareRows + recycleBinRows;
    }
}
