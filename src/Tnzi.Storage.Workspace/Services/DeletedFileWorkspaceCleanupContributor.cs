namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// 把「记录已删除、版本或分享还在」这一趟挂进父模块的后台清理。
/// </summary>
/// <remarks>
/// <para>
/// 记录删除时 <see cref="FileRecordDeletedEvent"/> 已经即时收过一次；这一趟兜的是事件没能完成的那些
/// （版本对象删不掉、重试用尽），以及这条清理出现之前就已删除的文件，那时删除不会顾及版本，
/// 旧版本的字节一直留在存储里，没有任何端点能再找到它们。
/// </para>
/// <para>
/// 候选按「行在、记录在任何租户里都不在」跨租户找（理由见 <see cref="DeletedFileWorkspaceCleaner"/>），
/// 所以不像别的贡献者那样逐租户切换上下文。一趟最多处理 <c>maxItems</c> 个文件。
/// </para>
/// </remarks>
public class DeletedFileWorkspaceCleanupContributor : IStorageCleanupContributor
{
    private readonly IRepository<FileRecord, Guid> _files;
    private readonly IRepository<FileVersion, Guid> _versions;
    private readonly IRepository<FileShare, Guid> _shares;
    private readonly IRepository<RecycleBinEntry, Guid> _recycleBinEntries;
    private readonly DeletedFileWorkspaceCleaner _cleaner;
    private readonly ILogger<DeletedFileWorkspaceCleanupContributor> _logger;

    public DeletedFileWorkspaceCleanupContributor(
        IRepository<FileRecord, Guid> files,
        IRepository<FileVersion, Guid> versions,
        IRepository<FileShare, Guid> shares,
        IRepository<RecycleBinEntry, Guid> recycleBinEntries,
        DeletedFileWorkspaceCleaner cleaner,
        ILogger<DeletedFileWorkspaceCleanupContributor> logger)
    {
        _files = Check.NotNull(files);
        _versions = Check.NotNull(versions);
        _shares = Check.NotNull(shares);
        _recycleBinEntries = Check.NotNull(recycleBinEntries);
        _cleaner = Check.NotNull(cleaner);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public string Name => "DeletedFileVersionsAndShares";

    /// <inheritdoc />
    public async Task<int> CleanupAsync(int maxItems, CancellationToken cancellationToken = default)
    {
        if (maxItems <= 0)
            return 0;

        var records = _files.AsQueryable().IgnoreQueryFilters();

        var fileIds = await _versions.AsQueryable()
            .IgnoreQueryFilters()
            .Where(v => !records.Any(f => f.Id == v.FileId))
            .Select(v => v.FileId)
            .Distinct()
            .Take(maxItems)
            .ToListAsync(cancellationToken);

        if (fileIds.Count < maxItems)
        {
            var shareFileIds = await _shares.AsQueryable()
                .IgnoreQueryFilters()
                .Where(s => !records.Any(f => f.Id == s.FileId))
                .Select(s => s.FileId)
                .Distinct()
                .Take(maxItems)
                .ToListAsync(cancellationToken);
            fileIds = fileIds.Union(shareFileIds).Take(maxItems).ToList();
        }

        if (fileIds.Count < maxItems)
        {
            var recycledFileIds = await _recycleBinEntries.AsQueryable()
                .IgnoreQueryFilters()
                .Where(e => e.ItemType == RecycleBinItemType.File && !records.Any(f => f.Id == e.ItemId))
                .Select(e => e.ItemId)
                .Distinct()
                .Take(maxItems)
                .ToListAsync(cancellationToken);
            fileIds = fileIds.Union(recycledFileIds).Take(maxItems).ToList();
        }

        var removed = 0;
        foreach (var fileId in fileIds)
        {
            try
            {
                removed += await _cleaner.PurgeAsync(fileId, cancellationToken);
            }
            catch (Exception ex)
            {
                // 一个文件的对象删不掉不该让这一趟的其余文件跟着停；它的行还在，下一趟再来。
                _logger.LogWarning("Failed to clean versions and shares of deleted file {FileId}: {Error}", fileId, ex.Message);
            }
        }

        return removed;
    }
}
