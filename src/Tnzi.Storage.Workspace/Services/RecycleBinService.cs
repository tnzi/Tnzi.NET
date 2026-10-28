namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// <see cref="IRecycleBinService"/> 的默认实现。
/// </summary>
/// <remarks>
/// <para>
/// <b>文件</b>进回收站：只插一条 <see cref="RecycleBinEntry"/>，记录本身一个字都不改（目录、字节、引用计数都留着），
/// 列表与搜索靠这一条把它摘掉（见 <see cref="FileSpaceQueries"/>）。<b>目录</b>进回收站：它与它下面每个还活着的目录
/// 一起软删并记下条目 id，里面的文件随目录一起消失。早先单独删掉的子目录、文件有自己的条目，还原上级时它们留在回收站里。
/// </para>
/// <para>
/// 永久删除以系统身份经 <see cref="IFileHoldService"/> 放掉文件空间对每个文件的持有：没有别的记录引用的文件连字节一起删，
/// 还被引用的（聊天消息的附件）留下，并从目录里摘出来，不再出现在文件空间里。目录行保持软删（与目录服务一直以来的删除一致）。
/// </para>
/// </remarks>
public class RecycleBinService : ApplicationService, IRecycleBinService
{
    private const string RootLocation = "/";

    /// <summary>还原目录撞名时最多试多少个序号。</summary>
    private const int MaxRenameAttempts = 100;

    private readonly IRepository<RecycleBinEntry, Guid> _entryRepository;
    private readonly IRepository<FileFolder, Guid> _folderRepository;
    private readonly IRepository<FileRecord, Guid> _fileRepository;
    private readonly IRepository<FileReference, Guid> _referenceRepository;
    private readonly IFileAccessAuthorizer _accessAuthorizer;
    private readonly IFileHoldService _holds;
    private readonly IOptionsMonitor<StorageOptions> _options;
    private readonly ICurrentTenant _currentTenant;
    private readonly bool _multiTenancyEnabled;

    public RecycleBinService(
        IServiceProvider serviceProvider,
        IRepository<RecycleBinEntry, Guid> entryRepository,
        IRepository<FileFolder, Guid> folderRepository,
        IRepository<FileRecord, Guid> fileRepository,
        IRepository<FileReference, Guid> referenceRepository,
        IFileAccessAuthorizer accessAuthorizer,
        IFileHoldService holds,
        IOptionsMonitor<StorageOptions> options,
        ICurrentTenant currentTenant,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(serviceProvider)
    {
        _entryRepository = Check.NotNull(entryRepository);
        _folderRepository = Check.NotNull(folderRepository);
        _fileRepository = Check.NotNull(fileRepository);
        _referenceRepository = Check.NotNull(referenceRepository);
        _accessAuthorizer = Check.NotNull(accessAuthorizer);
        _holds = Check.NotNull(holds);
        _options = Check.NotNull(options);
        _currentTenant = Check.NotNull(currentTenant);
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    public async Task<Result> MoveToRecycleBinAsync(IReadOnlyCollection<Guid>? fileIds, IReadOnlyCollection<Guid>? folderIds, CancellationToken cancellationToken = default)
    {
        if (CurrentUser?.Id is not { } actorId)
            return Fail("Sign in to delete files", 401, ErrorCodes.UNAUTHORIZED);

        var fileIdList = fileIds?.Distinct().ToList() ?? [];
        var folderIdList = folderIds?.Distinct().ToList() ?? [];
        if (fileIdList.Count == 0 && folderIdList.Count == 0)
            return Fail("Choose at least one file or folder to delete", 400, ErrorCodes.VALIDATION_ERROR);

        // ★ 先整批判定再动手：一项不行整批不动，回答与「不存在」同一句。删除是一次意图，做一半会留下调用方
        //   无从得知的状态；而区分「不存在」与「无权」等于告诉试探者这个 id 上有东西。
        var folders = await _folderRepository.AsQueryable(withTracking: true)
            .Where(f => folderIdList.Contains(f.Id))
            .ToListAsync(cancellationToken);
        if (folders.Count != folderIdList.Count)
            return NothingFound();

        foreach (var folder in folders)
        {
            // 没有所有者的目录（后台任务建的）不属于任何人的文件空间，也就没有回收站可进。
            if (folder.CreatorId == null || !await CanDeleteFolderAsync(folder))
                return NothingFound();
        }

        var files = await _fileRepository.AsQueryable()
            .Where(f => fileIdList.Contains(f.Id))
            .ToListAsync(cancellationToken);
        if (files.Count != fileIdList.Count)
            return NothingFound();

        foreach (var file in files)
        {
            // 删文件是对那条记录的写（与父模块 DELETE files/{id} 同一判据），且它此刻得在所有者的文件空间里：
            // 已经在回收站里的、只被业务记录持有的（聊天附件、头像）都不是文件空间能删的东西。
            if (file.CreatorId is not { } ownerId
                || !await _accessAuthorizer.CanWriteAsync(file, cancellationToken)
                || !await IsInSpaceAsync(file.Id, ownerId, cancellationToken))
            {
                return NothingFound();
            }
        }

        // 每个目录连同它下面还活着的目录一起走；被请求的目录若已在另一个被请求目录的子树里，就随那一个一起走。
        var subtrees = new Dictionary<Guid, List<FileFolder>>();
        foreach (var folder in folders)
        {
            subtrees[folder.Id] = await CollectLiveSubtreeAsync(folder, cancellationToken);
        }

        var roots = folders
            .Where(folder => !subtrees.Any(other => other.Key != folder.Id && other.Value.Any(f => f.Id == folder.Id)))
            .ToList();
        var trashedFolderIds = roots.SelectMany(root => subtrees[root.Id]).Select(f => f.Id).ToHashSet();

        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            var now = DateTime.UtcNow;

            foreach (var root in roots)
            {
                var subtree = subtrees[root.Id];
                var subtreeIds = subtree.Select(f => f.Id).ToList();
                var ownerId = root.CreatorId!.Value;

                var contents = FileSpaceQueries
                    .NotInRecycleBin(_fileRepository.AsQueryable(), _entryRepository.AsQueryable())
                    .Where(f => f.CreatorId == ownerId && f.FolderId != null && subtreeIds.Contains(f.FolderId.Value));

                var entry = new RecycleBinEntry
                {
                    OwnerId = ownerId,
                    ItemType = RecycleBinItemType.Folder,
                    ItemId = root.Id,
                    Name = root.Name,
                    OriginalParentId = root.ParentId,
                    OriginalLocation = await GetLocationAsync(root.ParentId, ct),
                    Size = await contents.SumAsync(f => (long?)f.Size, ct) ?? 0,
                    FileCount = await contents.CountAsync(ct)
                };
                await _entryRepository.InsertAsync(entry, ct);

                foreach (var folder in subtree)
                {
                    folder.RecycleBinEntryId = entry.Id;
                    folder.IsDeleted = true;
                    folder.DeletionTime = now;
                    folder.DeleterId = actorId;
                }

                await _folderRepository.UpdateManyAsync(subtree, ct);
            }

            foreach (var file in files)
            {
                // 一起选中的、本就在被删目录里的文件随目录走：给它再单独记一条，还原目录时它反而会留在回收站里。
                if (file.FolderId is { } folderId && trashedFolderIds.Contains(folderId))
                    continue;

                await _entryRepository.InsertAsync(new RecycleBinEntry
                {
                    OwnerId = file.CreatorId!.Value,
                    ItemType = RecycleBinItemType.File,
                    ItemId = file.Id,
                    Name = string.IsNullOrEmpty(file.OriginalName) ? file.FileName : file.OriginalName,
                    OriginalParentId = file.FolderId,
                    OriginalLocation = await GetLocationAsync(file.FolderId, ct),
                    Size = file.Size,
                    FileCount = 1
                }, ct);
            }
        }, cancellationToken);

        LogInformation("Moved {Files} file(s) and {Folders} folder(s) to the recycle bin", fileIdList.Count, roots.Count);
        return Ok();
    }

    public async Task<Result<IPagedList<RecycleBinEntryDto>>> GetEntriesAsync(RecycleBinQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        if (CurrentUser?.Id is not { } ownerId)
            return Fail<IPagedList<RecycleBinEntryDto>>("Sign in to see your recycle bin", 401, ErrorCodes.UNAUTHORIZED);

        var entries = _entryRepository.AsQueryable().Where(e => e.OwnerId == ownerId);
        var total = await entries.CountAsync(cancellationToken);
        var page = await entries
            .OrderByDescending(e => e.CreationTime)
            .ThenBy(e => e.Id)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);

        var fileIds = page.Where(e => e.ItemType == RecycleBinItemType.File).Select(e => e.ItemId).ToList();
        var fileTypes = fileIds.Count == 0
            ? []
            : await _fileRepository.AsQueryable()
                .Where(f => fileIds.Contains(f.Id))
                .Select(f => new { f.Id, f.Extension, f.ContentType })
                .ToDictionaryAsync(f => f.Id, cancellationToken);

        var retentionDays = _options.CurrentValue.RecycleBin.RetentionDays;
        var items = page.Select(e => new RecycleBinEntryDto
        {
            Id = e.Id,
            ItemType = e.ItemType,
            ItemId = e.ItemId,
            Name = e.Name,
            OriginalLocation = e.OriginalLocation,
            Size = e.Size,
            FileCount = e.FileCount,
            Extension = fileTypes.TryGetValue(e.ItemId, out var type) ? type.Extension : null,
            ContentType = fileTypes.TryGetValue(e.ItemId, out var type2) ? type2.ContentType : null,
            DeletedAt = e.CreationTime,
            PurgeAt = retentionDays > 0 ? e.CreationTime.AddDays(retentionDays) : null
        }).ToList();

        return Ok<IPagedList<RecycleBinEntryDto>>(new PagedList<RecycleBinEntryDto>(items, query.PageIndex, query.PageSize, total));
    }

    public async Task<Result<RecycleBinRestoreResult>> RestoreAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadOwnEntriesAsync(entryIds, cancellationToken);
        if (!loaded.Succeeded)
            return Fail<RecycleBinRestoreResult>(loaded.Message ?? "Recycle bin entry not found", loaded.Code ?? 404, loaded.ErrorCode);

        var result = new RecycleBinRestoreResult();
        try
        {
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                // 先目录后文件：一起还原的「目录 + 早先单独删掉的、原本在它里面的文件」，文件能回到还原出来的目录里。
                foreach (var entry in loaded.Data!.OrderBy(e => e.ItemType == RecycleBinItemType.Folder ? 0 : 1))
                {
                    if (entry.ItemType == RecycleBinItemType.Folder)
                        await RestoreFolderAsync(entry, result, ct);
                    else
                        await RestoreFileAsync(entry, result, ct);

                    await _entryRepository.DeleteAsync(entry.Id, ct);
                }
            }, cancellationToken);
        }
        catch (RestoreConflictException ex)
        {
            // 以异常离开工作单元才会回滚：一项还原不了，整批都不还原。
            return Fail<RecycleBinRestoreResult>(ex.Message, 409, ErrorCodes.DATA_CONFLICT);
        }

        LogInformation("Restored {Count} item(s) from the recycle bin", result.Restored);
        return Ok(result);
    }

    public async Task<Result<int>> PurgeAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadOwnEntriesAsync(entryIds, cancellationToken);
        if (!loaded.Succeeded)
            return Fail<int>(loaded.Message ?? "Recycle bin entry not found", loaded.Code ?? 404, loaded.ErrorCode);

        foreach (var entry in loaded.Data!)
        {
            await PurgeEntryAsync(entry, cancellationToken);
        }

        return Ok(loaded.Data!.Count);
    }

    public async Task<Result<int>> EmptyAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentUser?.Id is not { } ownerId)
            return Fail<int>("Sign in to empty your recycle bin", 401, ErrorCodes.UNAUTHORIZED);

        var entries = await _entryRepository.AsQueryable()
            .Where(e => e.OwnerId == ownerId)
            .OrderBy(e => e.CreationTime)
            .ToListAsync(cancellationToken);

        foreach (var entry in entries)
        {
            await PurgeEntryAsync(entry, cancellationToken);
        }

        LogInformation("Emptied the recycle bin of user {UserId}: {Count} item(s)", ownerId, entries.Count);
        return Ok(entries.Count);
    }

    public async Task<int> PurgeExpiredAsync(int maxEntries, CancellationToken cancellationToken = default)
    {
        var retentionDays = _options.CurrentValue.RecycleBin.RetentionDays;
        if (retentionDays <= 0 || maxEntries <= 0)
            return 0;

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        // 后台清理不在任何租户里：启用多租户时按租户逐个切上下文，让全局过滤器把每一趟收在一个租户里
        // （同 ExpiredUploadSessionCleanupContributor）。未启用时 TenantId 不是数据库列，直接单遍。
        if (!_multiTenancyEnabled)
            return await PurgeExpiredInCurrentTenantAsync(cutoff, maxEntries, cancellationToken);

        var tenantIds = await _entryRepository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(e => e.CreationTime < cutoff)
            .Select(e => e.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var total = 0;
        foreach (var tenantId in tenantIds)
        {
            using (_currentTenant.Change(tenantId))
            {
                total += await PurgeExpiredInCurrentTenantAsync(cutoff, maxEntries - total, cancellationToken);
            }

            if (total >= maxEntries)
                break;
        }

        return total;
    }

    private async Task<int> PurgeExpiredInCurrentTenantAsync(DateTime cutoff, int maxEntries, CancellationToken cancellationToken)
    {
        if (maxEntries <= 0)
            return 0;

        var expired = await _entryRepository.AsQueryable()
            .Where(e => e.CreationTime < cutoff)
            .OrderBy(e => e.CreationTime)
            .Take(maxEntries)
            .ToListAsync(cancellationToken);

        var purged = 0;
        foreach (var entry in expired)
        {
            // 一条清不掉（对象存储暂时不可用）不拖累其余的：它留在回收站里，下一趟再试。
            try
            {
                await PurgeEntryAsync(entry, cancellationToken);
                purged++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogWarning("Failed to purge expired recycle bin entry {EntryId}: {Error}", entry.Id, ex.Message);
            }
        }

        return purged;
    }

    /// <summary>
    /// 永久删除一条：先放掉文件空间对其中每个文件的持有（无人持有的连字节删掉），再删条目。
    /// 中途失败时条目还在，可以再删一次；已经删掉的文件再处理一次只是「找不到」。
    /// </summary>
    private async Task PurgeEntryAsync(RecycleBinEntry entry, CancellationToken cancellationToken)
    {
        if (entry.ItemType == RecycleBinItemType.File)
        {
            await ReleaseFromSpaceAsync(entry.ItemId, cancellationToken);
        }
        else
        {
            var folderIds = await _folderRepository.AsQueryable()
                .IgnoreQueryFilters()
                .Where(f => f.RecycleBinEntryId == entry.Id)
                .Select(f => f.Id)
                .ToListAsync(cancellationToken);

            // 目录里早先单独删掉的文件有自己的条目，不归这一条管（它们日后还原会回到根目录）。
            var ownerId = entry.OwnerId;
            var fileIds = folderIds.Count == 0
                ? []
                : await FileSpaceQueries
                    .NotInRecycleBin(_fileRepository.AsQueryable(), _entryRepository.AsQueryable())
                    .Where(f => f.CreatorId == ownerId && f.FolderId != null && folderIds.Contains(f.FolderId.Value))
                    .Select(f => f.Id)
                    .ToListAsync(cancellationToken);

            foreach (var fileId in fileIds)
            {
                await ReleaseFromSpaceAsync(fileId, cancellationToken);
            }

            // 目录行保持软删，只是不再属于任何回收站条目：与目录服务一直以来的删除同一个终态。
            if (folderIds.Count > 0)
            {
                await _folderRepository.EnsureTransactionStartedAsync(cancellationToken);
                await _folderRepository.AsQueryable()
                    .IgnoreQueryFilters()
                    .Where(f => f.RecycleBinEntryId == entry.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(f => f.RecycleBinEntryId, (Guid?)null), cancellationToken);
            }
        }

        await _entryRepository.DeleteAsync(entry.Id, cancellationToken);
    }

    /// <summary>
    /// 文件空间放手：放掉全部隐式持有者；还被别的记录引用而留下的，从目录里摘出来，不再出现在文件空间里。
    /// </summary>
    private async Task ReleaseFromSpaceAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var outcome = await _holds.ReleaseImplicitHoldsAsync(fileId, cancellationToken);
        if (outcome != FileReleaseOutcome.StillReferenced)
            return;

        // 只写这一列：整行写回会与并发的绑定 / 解绑互相覆盖（计数虽然整行写不进去，别的列照样会被写回旧值）。
        await _fileRepository.EnsureTransactionStartedAsync(cancellationToken);
        await _fileRepository.AsQueryable()
            .Where(f => f.Id == fileId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.FolderId, (Guid?)null), cancellationToken);
    }

    private async Task RestoreFileAsync(RecycleBinEntry entry, RecycleBinRestoreResult result, CancellationToken cancellationToken)
    {
        var record = await _fileRepository.GetAsync(entry.ItemId, cancellationToken);
        if (record == null)
            return; // 文件在回收站期间被别的途径删掉了：没有东西可还原，条目照删。

        if (record.FolderId is { } folderId)
        {
            var folder = await _folderRepository.GetAsync(folderId, cancellationToken);
            if (folder == null || folder.IsDeleted || folder.CreatorId != entry.OwnerId)
            {
                // 原来的目录不在了（删了、进了回收站）：回到根目录，并保证文件空间对它有一份持有 —— 根目录里的文件靠这一份才算在文件空间里。
                record.FolderId = null;
                await FileReferenceCounts.TryTakeImplicitHoldAsync(_fileRepository, _referenceRepository, record.Id, cancellationToken);
                await _fileRepository.UpdateAsync(record, cancellationToken);
                result.RestoredToRoot.Add(entry.Id);
            }
        }

        result.Restored++;
    }

    private async Task RestoreFolderAsync(RecycleBinEntry entry, RecycleBinRestoreResult result, CancellationToken cancellationToken)
    {
        // 同一次删除带走的整棵子树：它们是软删的，常规查询看不见，按条目 id 绕开全局过滤器找回。
        var subtree = await _folderRepository.AsQueryable(withTracking: true)
            .IgnoreQueryFilters()
            .Where(f => f.RecycleBinEntryId == entry.Id)
            .ToListAsync(cancellationToken);

        var root = subtree.FirstOrDefault(f => f.Id == entry.ItemId);
        if (root == null)
            return;

        var ownerId = root.CreatorId;

        FileFolder? parent = null;
        if (root.ParentId is { } parentId)
        {
            parent = await _folderRepository.GetAsync(parentId, cancellationToken);
            if (parent == null || parent.IsDeleted || parent.CreatorId != ownerId)
            {
                parent = null;
                result.RestoredToRoot.Add(entry.Id);
            }
        }

        // 原位置已有同名目录（删掉之后又建了一个）：名字后面加序号，而不是让还原失败 —— 两个都是用户的东西。
        var name = root.Name;
        var path = FileFolderService.BuildPath(name, parent);
        var attempt = 1;
        while (await _folderRepository.AnyAsync(f => f.Path == path && f.CreatorId == ownerId && !f.IsDeleted, cancellationToken))
        {
            attempt++;
            if (attempt > MaxRenameAttempts)
                throw new RestoreConflictException($"Cannot restore \"{root.Name}\": too many folders with the same name at its original location.");

            name = $"{root.Name} ({attempt})";
            path = FileFolderService.BuildPath(name, parent);
        }

        if (name != root.Name)
            result.Renamed[entry.Id] = name;

        root.Name = name;
        root.ParentId = parent?.Id;
        root.Path = path;

        // 子树里每个目录的路径从还原后的根重新拼：删除期间上级可能改过名或挪过位置，存着的路径已经过时。
        var childrenByParent = subtree.Where(f => f.ParentId != null).ToLookup(f => f.ParentId!.Value);
        var pending = new Queue<FileFolder>([root]);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var child in childrenByParent[current.Id])
            {
                child.Path = $"{current.Path}/{child.Name}";
                pending.Enqueue(child);
            }
        }

        var subtreeIds = subtree.Select(f => f.Id).ToList();
        var descendantPaths = subtree.Where(f => f.Id != root.Id).Select(f => f.Path).ToList();
        if (descendantPaths.Count > 0 && await _folderRepository.AnyAsync(
                f => descendantPaths.Contains(f.Path) && f.CreatorId == ownerId && !subtreeIds.Contains(f.Id) && !f.IsDeleted,
                cancellationToken))
        {
            throw new RestoreConflictException($"Cannot restore \"{root.Name}\": a folder inside it collides with an existing folder path.");
        }

        foreach (var folder in subtree)
        {
            folder.IsDeleted = false;
            folder.DeletionTime = null;
            folder.DeleterId = null;
            folder.RecycleBinEntryId = null;
        }

        await _folderRepository.UpdateManyAsync(subtree, cancellationToken);
        result.Restored++;
    }

    /// <summary>当前用户回收站里的这几条；有一条不是（不存在 / 是别人的）整批答 404。</summary>
    private async Task<Result<List<RecycleBinEntry>>> LoadOwnEntriesAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken)
    {
        if (CurrentUser?.Id is not { } ownerId)
            return Fail<List<RecycleBinEntry>>("Sign in to manage your recycle bin", 401, ErrorCodes.UNAUTHORIZED);

        var ids = entryIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0)
            return Fail<List<RecycleBinEntry>>("Choose at least one item from the recycle bin", 400, ErrorCodes.VALIDATION_ERROR);

        var entries = await _entryRepository.AsQueryable()
            .Where(e => ids.Contains(e.Id) && e.OwnerId == ownerId)
            .ToListAsync(cancellationToken);
        if (entries.Count != ids.Count)
            return Fail<List<RecycleBinEntry>>("Recycle bin entry not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        return Ok(entries);
    }

    /// <summary>这个文件此刻在 <paramref name="ownerId"/> 的文件空间里吗（判据同列表与搜索）。</summary>
    private Task<bool> IsInSpaceAsync(Guid fileId, Guid ownerId, CancellationToken cancellationToken)
        => FileSpaceQueries
            .InSpace(
                _fileRepository.AsQueryable().Where(f => f.Id == fileId),
                ownerId,
                _folderRepository.AsQueryable(),
                _referenceRepository.AsQueryable(),
                _entryRepository.AsQueryable())
            .AnyAsync(cancellationToken);

    /// <summary>一个目录（还活着）的整棵子树，含它自己，全部带跟踪（随后要改它们）。</summary>
    private async Task<List<FileFolder>> CollectLiveSubtreeAsync(FileFolder root, CancellationToken cancellationToken)
    {
        var collected = new List<FileFolder> { root };
        var frontier = new List<Guid> { root.Id };
        while (frontier.Count > 0)
        {
            var parentIds = frontier;
            var level = await _folderRepository.AsQueryable(withTracking: true)
                .Where(f => f.ParentId != null && parentIds.Contains(f.ParentId.Value))
                .ToListAsync(cancellationToken);
            collected.AddRange(level);
            frontier = level.Select(f => f.Id).ToList();
        }

        return collected;
    }

    private async Task<string> GetLocationAsync(Guid? folderId, CancellationToken cancellationToken)
    {
        if (folderId is not { } id)
            return RootLocation;

        var folder = await _folderRepository.GetAsync(id, cancellationToken);
        return folder?.Path ?? RootLocation;
    }

    /// <summary>
    /// 能不能删这个目录：自己的，或持 <c>storage.file.delete</c>（与目录服务的删除同一判据）。
    /// 没有权限体系（未加载 Authorization）时只认归属。
    /// </summary>
    private async Task<bool> CanDeleteFolderAsync(FileFolder folder)
    {
        if (folder.CreatorId.HasValue && CurrentUser?.Id is { } me && folder.CreatorId.Value == me)
            return true;

        return PermissionChecker != null && await PermissionChecker.IsGrantedAsync(StoragePermissionNames.FileDelete);
    }

    private Result NothingFound() => Fail("No files or folders found with the specified IDs", 404, ErrorCodes.RESOURCE_NOT_FOUND);

    /// <summary>还原中途撞上冲突：带着原因离开工作单元，让整批回滚，在外层翻译回 409。</summary>
    private sealed class RestoreConflictException(string message) : Exception(message);
}
