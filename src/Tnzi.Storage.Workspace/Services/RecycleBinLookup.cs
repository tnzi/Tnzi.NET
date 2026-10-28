namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// 一个文件此刻是不是在回收站里：它自己被删了，或它所在的目录随某一条回收站条目进了回收站。
/// </summary>
/// <remarks>
/// 父模块不认识回收站：回收站里的文件仍是一条完整的记录，所有者照样能按 id 下载。本模块自己的几条对外通路
/// 要把它当作不存在 —— 分享链接（删掉的文件不该继续对外可取）、归档（挪进目录不能绕开回收站把它变回来）。
/// </remarks>
public class RecycleBinLookup
{
    private readonly IRepository<RecycleBinEntry, Guid> _entries;
    private readonly IRepository<FileFolder, Guid> _folders;

    public RecycleBinLookup(IRepository<RecycleBinEntry, Guid> entries, IRepository<FileFolder, Guid> folders)
    {
        _entries = Check.NotNull(entries);
        _folders = Check.NotNull(folders);
    }

    /// <summary>这个文件是否在回收站里（直接，或随它的目录）。</summary>
    public async Task<bool> IsInRecycleBinAsync(FileRecord file, CancellationToken cancellationToken = default)
    {
        Check.NotNull(file);

        if (await _entries.AnyAsync(e => e.ItemType == RecycleBinItemType.File && e.ItemId == file.Id, cancellationToken))
            return true;

        if (file.FolderId is not { } folderId)
            return false;

        // 进了回收站的目录是软删的，常规查询看不见它，所以这里绕开全局过滤器去看那一列。
        return await _folders.AsQueryable()
            .IgnoreQueryFilters()
            .AnyAsync(f => f.Id == folderId && f.IsDeleted && f.RecycleBinEntryId != null, cancellationToken);
    }

    /// <summary>这批文件里在回收站中的那些（直接，或随它的目录）的 id。</summary>
    public async Task<HashSet<Guid>> FindInRecycleBinAsync(IReadOnlyCollection<FileRecord> files, CancellationToken cancellationToken = default)
    {
        Check.NotNull(files);
        if (files.Count == 0)
            return [];

        var ids = files.Select(f => f.Id).ToList();
        var direct = await _entries.AsQueryable()
            .Where(e => e.ItemType == RecycleBinItemType.File && ids.Contains(e.ItemId))
            .Select(e => e.ItemId)
            .ToListAsync(cancellationToken);

        var folderIds = files.Where(f => f.FolderId != null).Select(f => f.FolderId!.Value).Distinct().ToList();
        var trashedFolders = folderIds.Count == 0
            ? []
            : await _folders.AsQueryable()
                .IgnoreQueryFilters()
                .Where(f => folderIds.Contains(f.Id) && f.IsDeleted && f.RecycleBinEntryId != null)
                .Select(f => f.Id)
                .ToListAsync(cancellationToken);

        var result = direct.ToHashSet();
        foreach (var file in files)
        {
            if (file.FolderId is { } folderId && trashedFolders.Contains(folderId))
                result.Add(file.Id);
        }

        return result;
    }
}
