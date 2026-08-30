namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// File folder service implementation for virtual directory management
/// </summary>
public class FileFolderService : ApplicationService, IFileFolderService
{
    private readonly IRepository<FileFolder, Guid> _folderRepository;
    private readonly IRepository<FileRecord, Guid> _fileRepository;
    private readonly IFileAccessAuthorizer _accessAuthorizer;

    public FileFolderService(
        IServiceProvider serviceProvider,
        IRepository<FileFolder, Guid> folderRepository,
        IRepository<FileRecord, Guid> fileRepository,
        IFileAccessAuthorizer accessAuthorizer)
        : base(serviceProvider)
    {
        _folderRepository = Check.NotNull(folderRepository);
        _fileRepository = Check.NotNull(fileRepository);
        _accessAuthorizer = Check.NotNull(accessAuthorizer);
    }

    public async Task<Result<FileFolderDto>> CreateAsync(CreateFileFolderDto input)
    {
        Check.NotNull(input);
        Check.NotNullOrWhiteSpace(input.Name);

        // Validate parent exists if specified
        if (input.ParentId.HasValue)
        {
            var parent = await _folderRepository.GetAsync(input.ParentId.Value);

            // 不可见的父目录与不存在的父目录给同一个回答：403 会确认这个 id 上确实有目录，
            // 而目录 id 是顺序 GUID，那本身就是可枚举信息（同 docs/modules/storage.md 的口径）。
            if (parent == null || !await CanAccessAsync(parent, StoragePermissionNames.FileCreate))
            {
                return Fail<FileFolderDto>("Parent folder not found", 404);
            }
        }

        // Build path
        var path = await BuildPathAsync(input.Name, input.ParentId);

        // Check for duplicate path
        var exists = await _folderRepository.AnyAsync(f => f.Path == path && !f.IsDeleted);
        if (exists)
        {
            return Fail<FileFolderDto>("A folder with the same name already exists at this location", 409);
        }

        var folder = new FileFolder
        {
            Name = input.Name.Trim(),
            ParentId = input.ParentId,
            Path = path,
            SortOrder = input.SortOrder,
            Description = input.Description
        };

        await _folderRepository.InsertAsync(folder);

        // EventBus 为空表示宿主未加载 EventBus 模块（可选依赖），此时跳过发布
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new FileFolderCreatedEvent
            {
                FolderId = folder.Id,
                Name = folder.Name,
                ParentId = folder.ParentId,
                Path = folder.Path
            });
        }

        return Ok(await MapToDto(folder));
    }

    public async Task<Result<FileFolderDto>> UpdateAsync(Guid id, UpdateFileFolderDto input)
    {
        Check.NotNull(input);

        var folder = await _folderRepository.GetAsync(id);
        if (folder == null)
        {
            return Fail<FileFolderDto>("Folder not found", 404);
        }

        // 看不到的目录与不存在的目录给同一个回答：403 会确认这个 id 上确实有目录。
        if (!await CanAccessAsync(folder, StoragePermissionNames.FileUpdate))
        {
            return Fail<FileFolderDto>("Folder not found", 404);
        }

        var newName = folder.Name;
        var nameChanged = false;

        if (input.Name != null)
        {
            Check.NotNullOrWhiteSpace(input.Name);
            var trimmedName = input.Name.Trim();
            if (trimmedName != folder.Name)
            {
                nameChanged = true;
                newName = trimmedName;
            }
        }

        var oldPath = folder.Path;
        var newPath = oldPath;

        // 重名冲突必须在写回实体之前判定：folder 处于 DbContext 跟踪中，
        // 提前赋值会让"校验失败"分支把脏值随外层 UoW 一起提交。
        if (nameChanged)
        {
            newPath = await BuildPathAsync(newName, folder.ParentId);

            var exists = await _folderRepository.AnyAsync(
                f => f.Path == newPath && f.Id != id && !f.IsDeleted);
            if (exists)
            {
                return Fail<FileFolderDto>("A folder with the same name already exists at this location", 409);
            }
        }

        folder.Name = newName;

        if (input.Description != null)
        {
            folder.Description = input.Description;
        }

        if (input.SortOrder.HasValue)
        {
            folder.SortOrder = input.SortOrder.Value;
        }

        if (nameChanged)
        {
            folder.Path = newPath;

            // Atomically update descendant paths together with the folder itself so a partial
            // failure cannot leave the tree inconsistent.
            await ExecuteInUnitOfWorkAsync(async ct =>
            {
                await UpdateDescendantPathsAsync(oldPath, folder.Path);
                await _folderRepository.UpdateAsync(folder, ct);
            });
        }
        else
        {
            await _folderRepository.UpdateAsync(folder);
        }

        return Ok(await MapToDto(folder));
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var folder = await _folderRepository.GetAsync(id);
        if (folder == null)
        {
            return Fail("Folder not found", 404);
        }

        if (!await CanAccessAsync(folder, StoragePermissionNames.FileDelete))
        {
            return Fail("Folder not found", 404);
        }

        // Check for child folders
        var hasChildren = await _folderRepository.AnyAsync(f => f.ParentId == id && !f.IsDeleted);
        if (hasChildren)
        {
            return Fail("Cannot delete a folder that contains sub-folders. Please delete or move sub-folders first.", 400);
        }

        // Check for files in the folder
        var hasFiles = await _fileRepository.AnyAsync(f => f.FolderId == id);
        if (hasFiles)
        {
            return Fail("Cannot delete a folder that contains files. Please move or delete files first.", 400);
        }

        await _folderRepository.DeleteAsync(folder);

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new FileFolderDeletedEvent
            {
                FolderId = folder.Id,
                Name = folder.Name,
                Path = folder.Path
            });
        }

        return Ok();
    }

    public async Task<Result<FileFolderDto>> GetAsync(Guid id)
    {
        var folder = await _folderRepository.GetAsync(id);
        if (folder == null)
        {
            return Fail<FileFolderDto>("Folder not found", 404);
        }

        if (!await CanAccessAsync(folder, StoragePermissionNames.FileView))
        {
            return Fail<FileFolderDto>("Folder not found", 404);
        }

        return Ok(await MapToDto(folder));
    }

    public async Task<Result<List<FileFolderDto>>> GetTreeAsync(Guid? parentId = null)
    {
        // 权限码查一次而不是逐行查：持 storage.file.view 的管理端看整棵树，
        // 其余人只看自己的目录。逐行去问权限检查器，一棵百来个节点的树就是一百次往返。
        var seesEverything = await HasPermissionAsync(StoragePermissionNames.FileView);

        var query = _folderRepository.AsQueryable().Where(f => !f.IsDeleted);

        if (!seesEverything)
        {
            // 未登录（或无主目录）不属于任何人，只有持码的管理员看得到 —— 与 IsOwner 同口径。
            if (CurrentUser?.Id is not { } me)
            {
                return Ok(new List<FileFolderDto>());
            }

            query = query.Where(f => f.CreatorId == me);
        }

        var allFolders = await query
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Name)
            .ToListAsync();

        // Count files per folder
        var fileCounts = await _fileRepository.AsQueryable()
            .Where(f => f.FolderId != null)
            .GroupBy(f => f.FolderId!.Value)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.Count);

        var tree = BuildTree(allFolders, fileCounts, parentId);
        return Ok(tree);
    }

    public async Task<Result> MoveAsync(Guid id, Guid? newParentId)
    {
        var folder = await _folderRepository.GetAsync(id);
        if (folder == null)
        {
            return Fail("Folder not found", 404);
        }

        if (!await CanAccessAsync(folder, StoragePermissionNames.FileUpdate))
        {
            return Fail("Folder not found", 404);
        }

        // Cannot move to itself
        if (newParentId == id)
        {
            return Fail("Cannot move a folder into itself", 400);
        }

        // Validate new parent exists
        if (newParentId.HasValue)
        {
            var newParent = await _folderRepository.GetAsync(newParentId.Value);
            if (newParent == null || !await CanAccessAsync(newParent, StoragePermissionNames.FileUpdate))
            {
                return Fail("Target parent folder not found", 404);
            }


            // Prevent circular reference: new parent cannot be a descendant of this folder
            if (await IsDescendantOfAsync(newParentId.Value, id))
            {
                return Fail("Cannot move a folder into one of its own sub-folders", 400);
            }
        }

        var oldParentId = folder.ParentId;
        var oldPath = folder.Path;

        // 先算出新路径并校验冲突，确认无冲突后才写回实体：folder 处于 DbContext 跟踪中，
        // 提前赋值会让"校验失败"分支把脏值随外层 UoW 一起提交。
        var newPath = await BuildPathAsync(folder.Name, newParentId);

        // Check for duplicate path at new location
        var exists = await _folderRepository.AnyAsync(
            f => f.Path == newPath && f.Id != id && !f.IsDeleted);
        if (exists)
        {
            return Fail("A folder with the same name already exists at the target location", 409);
        }

        folder.ParentId = newParentId;
        folder.Path = newPath;

        // Atomically update the folder itself and all descendant paths so a mid-way failure
        // cannot leave the folder tree in an inconsistent state.
        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            await UpdateDescendantPathsAsync(oldPath, folder.Path);
            await _folderRepository.UpdateAsync(folder, ct);
        });

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new FileFolderMovedEvent
            {
                FolderId = folder.Id,
                Name = folder.Name,
                OldParentId = oldParentId,
                NewParentId = newParentId,
                OldPath = oldPath,
                NewPath = folder.Path
            });
        }

        return Ok();
    }

    public async Task<Result> MoveFilesToFolderAsync(List<Guid> fileIds, Guid? folderId)
    {
        Check.NotNullOrEmpty(fileIds);

        // Validate target folder exists
        if (folderId.HasValue)
        {
            var folder = await _folderRepository.GetAsync(folderId.Value);
            if (folder == null || !await CanAccessAsync(folder, StoragePermissionNames.FileUpdate))
            {
                return Fail("Target folder not found", 404);
            }
        }

        // Update all files' FolderId
        var files = await _fileRepository.AsQueryable()
            .Where(f => fileIds.Contains(f.Id))
            .ToListAsync();

        if (files.Count == 0)
        {
            return Fail("No files found with the specified IDs", 404);
        }

        // ★ 重新归档别人的文件是对**那条记录**的写操作，判据因此是文件侧的
        // IFileAccessAuthorizer（模块自己声明的强制门），不是目录的归属。
        // 少了这一句，任何已登录用户都能把任意 id 的文件挪走或抽出目录 ——
        // 而文件 id 是顺序 GUID，IFileAccessAuthorizer 的注释正是为此写的
        //「仅凭知道 id 不足以构成授权」。
        //
        // 一条不行整批拒绝，而不是挪走能挪的那部分：批量归档是一次意图，
        // 做一半会留下调用方无从得知的、一半在新目录一半在旧目录的状态。
        // 回答沿用下面那句既有的 404 —— 403 会确认这个 id 上确实有文件。
        foreach (var file in files)
        {
            if (!await _accessAuthorizer.CanWriteAsync(file))
            {
                return Fail("No files found with the specified IDs", 404);
            }
        }

        foreach (var file in files)
        {
            file.FolderId = folderId;
        }

        await _fileRepository.UpdateManyAsync(files);

        return Ok();
    }

    #region Private Methods

    /// <summary>
    /// 归属判定，与 <c>FileAccessAuthorizer.IsOwner</c> 逐字同一条规则：
    /// <c>CreatorId</c> 为 null 的行（后台任务 / 迁移数据产生）不视为任何人所有，
    /// 只能由持权限码的管理员访问 —— 比「无主即公开」保守。
    /// </summary>
    private bool IsOwner(FileFolder folder)
        => folder.CreatorId.HasValue
           && CurrentUser?.Id is { } me
           && folder.CreatorId.Value == me;

    /// <summary>
    /// 能不能对这个目录做这件事：<b>自己的</b> 或 <b>持对应权限码</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与文件侧 <see cref="IFileAccessAuthorizer"/> 同构，但**刻意不去拓宽那个接口** ——
    /// 它的三个成员都吃 <c>FileRecord</c>，而它是 <c>TryAddScoped</c> 注册的、明摆着让消费方
    /// 替换的扩展点，加一个方法会把每一份自定义实现打编译不过。目录的判据留在服务内部。
    /// </para>
    /// <para>
    /// ★ <b>权限码按操作分开传</b>，与管理端控制器逐个端点挂的码对齐
    /// （view / create / update / delete）。统一用 <c>.update</c> 会让一个只持
    /// <c>storage.file.delete</c> 的管理员删不掉目录 —— 而那正是他被授权做的事。
    /// </para>
    /// <para>
    /// 未加载 Authorization 模块时没有 <c>IPermissionChecker</c>，此时保守拒绝：
    /// 没有权限体系的部署里「归属」是唯一可信的判据（同 <c>FileAccessAuthorizer</c>）。
    /// </para>
    /// </remarks>
    private async Task<bool> CanAccessAsync(FileFolder folder, string permissionName)
    {
        if (IsOwner(folder))
            return true;

        return await HasPermissionAsync(permissionName);
    }

    private async Task<bool> HasPermissionAsync(string permissionName)
    {
        var checker = PermissionChecker;
        if (checker == null)
            return false;

        return await checker.IsGrantedAsync(permissionName);
    }

    /// <summary>
    /// Build hierarchical path from parent chain
    /// </summary>
    private async Task<string> BuildPathAsync(string name, Guid? parentId)
    {
        if (!parentId.HasValue)
        {
            return $"/{name.Trim()}";
        }

        var parent = await _folderRepository.GetAsync(parentId.Value);
        var parentPath = parent?.Path ?? string.Empty;
        return $"{parentPath}/{name.Trim()}";
    }

    /// <summary>
    /// Check if candidateId is a descendant of ancestorId
    /// </summary>
    private async Task<bool> IsDescendantOfAsync(Guid candidateId, Guid ancestorId)
    {
        var current = await _folderRepository.GetAsync(candidateId);
        while (current != null && current.ParentId.HasValue)
        {
            if (current.ParentId.Value == ancestorId)
            {
                return true;
            }
            current = await _folderRepository.GetAsync(current.ParentId.Value);
        }
        return false;
    }

    /// <summary>
    /// Update paths for all descendants when a folder's path changes
    /// </summary>
    private async Task UpdateDescendantPathsAsync(string oldPath, string newPath)
    {
        var prefix = oldPath + "/";
        var descendants = await _folderRepository.AsQueryable()
            .Where(f => f.Path.StartsWith(prefix) && !f.IsDeleted)
            .ToListAsync();

        foreach (var descendant in descendants)
        {
            descendant.Path = newPath + descendant.Path[oldPath.Length..];
        }

        if (descendants.Count > 0)
        {
            await _folderRepository.UpdateManyAsync(descendants);
        }
    }

    /// <summary>
    /// Build tree structure from flat list
    /// </summary>
    private static List<FileFolderDto> BuildTree(
        List<FileFolder> folders,
        Dictionary<Guid, int> fileCounts,
        Guid? parentId)
    {
        var result = new List<FileFolderDto>();

        var children = folders.Where(f => f.ParentId == parentId).ToList();

        foreach (var child in children)
        {
            var dto = new FileFolderDto
            {
                Id = child.Id,
                Name = child.Name,
                ParentId = child.ParentId,
                Path = child.Path,
                SortOrder = child.SortOrder,
                Description = child.Description,
                FileCount = fileCounts.GetValueOrDefault(child.Id),
                Children = BuildTree(folders, fileCounts, child.Id)
            };

            result.Add(dto);
        }

        return result;
    }

    /// <summary>
    /// Map entity to DTO with file count
    /// </summary>
    private async Task<FileFolderDto> MapToDto(FileFolder folder)
    {
        var fileCount = await _fileRepository.CountAsync(f => f.FolderId == folder.Id);

        return new FileFolderDto
        {
            Id = folder.Id,
            Name = folder.Name,
            ParentId = folder.ParentId,
            Path = folder.Path,
            SortOrder = folder.SortOrder,
            Description = folder.Description,
            FileCount = fileCount
        };
    }

    #endregion

    public async Task<Result> ReorderAsync(IReadOnlyList<Guid> ids, Guid? parentId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(ids);

        var result = await ExecuteInUnitOfWorkAsync(
            ct => _folderRepository.ReorderAsync(ids, f => f.ParentId == parentId, ct),
            cancellationToken);

        if (!result.Succeeded)
            return Result.Failure(result.Message ?? "Reorder failed.", result.Code ?? 400, result.ErrorCode);

        LogInformation("Reordered {Count} folder(s) under parent {ParentId}", result.Data, parentId);
        return Ok();
    }
}
