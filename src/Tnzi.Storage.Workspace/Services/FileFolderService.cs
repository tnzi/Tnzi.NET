namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// File folder service implementation for virtual directory management
/// </summary>
public class FileFolderService : ApplicationService, IFileFolderService
{
    private readonly IRepository<FileFolder, Guid> _folderRepository;
    private readonly IRepository<FileRecord, Guid> _fileRepository;
    private readonly IRepository<FileReference, Guid> _referenceRepository;
    private readonly IRepository<RecycleBinEntry, Guid> _recycleBinRepository;
    private readonly IFileAccessAuthorizer _accessAuthorizer;
    private readonly RecycleBinLookup _recycleBin;

    public FileFolderService(
        IServiceProvider serviceProvider,
        IRepository<FileFolder, Guid> folderRepository,
        IRepository<FileRecord, Guid> fileRepository,
        IRepository<FileReference, Guid> referenceRepository,
        IRepository<RecycleBinEntry, Guid> recycleBinRepository,
        IFileAccessAuthorizer accessAuthorizer,
        RecycleBinLookup recycleBin)
        : base(serviceProvider)
    {
        _folderRepository = Check.NotNull(folderRepository);
        _fileRepository = Check.NotNull(fileRepository);
        _referenceRepository = Check.NotNull(referenceRepository);
        _recycleBinRepository = Check.NotNull(recycleBinRepository);
        _accessAuthorizer = Check.NotNull(accessAuthorizer);
        _recycleBin = Check.NotNull(recycleBin);
    }

    public async Task<Result<FileFolderDto>> CreateAsync(CreateFileFolderDto input)
    {
        Check.NotNull(input);
        Check.NotNullOrWhiteSpace(input.Name);

        // 名字要在进入路径拼装之前过形态检查：`archive/alpha` 会在根下造出一条
        // Path 长在别人前缀里的行，让受害者之后的每一次改名 / 移动都撞唯一索引。
        var name = input.Name.Trim();
        if (!FolderNameValidator.IsValid(name))
        {
            return Fail<FileFolderDto>(FolderNameValidator.InvalidNameMessage, 400);
        }

        // Validate parent exists if specified
        FileFolder? parent = null;
        if (input.ParentId.HasValue)
        {
            parent = await _folderRepository.GetAsync(input.ParentId.Value);

            // 不可见的父目录与不存在的父目录给同一个回答：403 会确认这个 id 上确实有目录，
            // 而目录 id 是顺序 GUID，那本身就是可枚举信息（同 docs/modules/storage.md 的口径）。
            if (parent == null || !await CanAccessAsync(parent, StoragePermissionNames.FileCreate))
            {
                return Fail<FileFolderDto>("Parent folder not found", 404);
            }
        }

        // ★ 子目录跟着父目录的所有者走，不跟着动手的人走：管理员替用户在他的目录下建子目录，建出来的仍是那个用户的，
        //   否则同一棵树上会混着两个人的目录，按所有者唯一的路径在它们之间互不约束。根目录归当前用户。
        var ownerId = parent?.CreatorId ?? CurrentUser?.Id;

        // Build path
        var path = BuildPath(name, parent);

        // 撞名只在同一个所有者的命名空间里判：别人的 /Documents 与这里无关，也不该从这里被确认存在。
        var exists = await _folderRepository.AnyAsync(f => f.Path == path && f.CreatorId == ownerId && !f.IsDeleted);
        if (exists)
        {
            return Fail<FileFolderDto>("A folder with the same name already exists at this location", 409);
        }

        var folder = new FileFolder
        {
            Name = name,
            ParentId = input.ParentId,
            Path = path,
            SortOrder = input.SortOrder,
            Description = input.Description,
            // 审计钩子只在 CreatorId 为 null 时填当前用户，所以这里写下的所有者会原样落库。
            CreatorId = ownerId
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
            if (!FolderNameValidator.IsValid(trimmedName))
            {
                return Fail<FileFolderDto>(FolderNameValidator.InvalidNameMessage, 400);
            }

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
        List<FileFolder> descendants = [];
        if (nameChanged)
        {
            newPath = await BuildPathAsync(newName, folder.ParentId);

            var ownerId = folder.CreatorId;
            var exists = await _folderRepository.AnyAsync(
                f => f.Path == newPath && f.CreatorId == ownerId && f.Id != id && !f.IsDeleted);
            if (exists)
            {
                return Fail<FileFolderDto>("A folder with the same name already exists at this location", 409);
            }

            // 后代的新路径同样要预检：自身路径空着不代表 `newPath/xxx` 也空着，
            // 撞上唯一索引的是提交那一刻的 500，而且每次重试都一样。
            descendants = await PlanDescendantPathsAsync(folder, newPath);
            if (await DescendantPathsCollideAsync(descendants, ownerId))
            {
                return Fail<FileFolderDto>("Renaming this folder would collide with an existing folder path under the new name", 409);
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
                await WriteDescendantPathsAsync(descendants, ct);
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

        // Check for files in the folder. 已经单独放进回收站的文件不算：界面上这个目录看起来是空的，
        // 而它们被还原时目录若已不在，会回到根目录。要连内容一起删，走回收站（IRecycleBinService）。
        var hasFiles = await FileSpaceQueries
            .NotInRecycleBin(_fileRepository.AsQueryable(), _recycleBinRepository.AsQueryable())
            .AnyAsync(f => f.FolderId == id);
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

    public async Task<Result<List<FileFolderDto>>> GetTreeAsync(Guid? parentId = null, Guid? ownerId = null)
    {
        // 权限码查一次而不是逐行查：持 storage.file.view 的管理端看整棵树（可按所有者收窄），
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

            // 非管理员问别人的树得到的是一棵空树，而不是一个确认「这个人有目录」的拒绝。
            if (ownerId.HasValue && ownerId.Value != me)
            {
                return Ok(new List<FileFolderDto>());
            }

            query = query.Where(f => f.CreatorId == me);
        }
        else if (ownerId.HasValue)
        {
            var owner = ownerId.Value;
            query = query.Where(f => f.CreatorId == owner);
        }

        var allFolders = await query
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Name)
            .ToListAsync();

        // Count files per folder：只数这棵树里的目录、且不在回收站里的文件（回收站里的文件在界面上不在目录里）。
        var folderIds = query.Select(f => f.Id);
        var fileCounts = await FileSpaceQueries
            .NotInRecycleBin(_fileRepository.AsQueryable(), _recycleBinRepository.AsQueryable())
            .Where(f => f.FolderId != null && folderIds.Contains(f.FolderId.Value))
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

            // 目录只能挪到同一个所有者的目录下：每个用户是一个独立的命名空间，跨所有者挂接会让一棵树里混着两个人的目录。
            // 只有能同时访问两边的人（管理员）走得到这一步，所以说清楚原因不泄漏任何东西。
            if (newParent.CreatorId != folder.CreatorId)
            {
                return Fail("A folder can only be moved into a folder of the same owner", 400);
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

        // Check for duplicate path at new location (within the owner's namespace)
        var ownerId = folder.CreatorId;
        var exists = await _folderRepository.AnyAsync(
            f => f.Path == newPath && f.CreatorId == ownerId && f.Id != id && !f.IsDeleted);
        if (exists)
        {
            return Fail("A folder with the same name already exists at the target location", 409);
        }

        // 后代的新路径同样要预检（理由见 UpdateAsync）。
        var descendants = await PlanDescendantPathsAsync(folder, newPath);
        if (await DescendantPathsCollideAsync(descendants, ownerId))
        {
            return Fail("Moving this folder would collide with an existing folder path at the target location", 409);
        }

        folder.ParentId = newParentId;
        folder.Path = newPath;

        // Atomically update the folder itself and all descendant paths so a mid-way failure
        // cannot leave the folder tree in an inconsistent state.
        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            await WriteDescendantPathsAsync(descendants, ct);
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
        FileFolder? targetFolder = null;
        if (folderId.HasValue)
        {
            targetFolder = await _folderRepository.GetAsync(folderId.Value);
            if (targetFolder == null || !await CanAccessAsync(targetFolder, StoragePermissionNames.FileUpdate))
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

        // 回收站里的文件不能靠挪动把它变回来（还原走回收站），回答同上一句：对调用方它此刻不在文件空间里。
        if ((await _recycleBin.FindInRecycleBinAsync(files)).Count > 0)
        {
            return Fail("No files found with the specified IDs", 404);
        }

        // ★ 文件只能放进它的所有者自己的目录：目录是按所有者分开的命名空间，别人目录里的文件在两个人的文件空间里
        //   都不出现（见 FileSpaceQueries）。只有能同时写两边的人（管理员）走得到这里，说清楚原因不泄漏任何东西。
        if (targetFolder != null && files.Any(f => f.CreatorId != targetFolder.CreatorId))
        {
            return Fail("Files can only be placed in folders that belong to the same owner as the files", 400);
        }

        // ★ 转正与归档在同一个工作单元里：转正是立即生效的条件写，归档是随后的整批保存。
        // 分开提交的话，批量保存失败时已经转正的临时记录不会跟着回滚，留下「转正了、没挪动」的记录；
        // 包在一起，要么全部转正并挪进目标目录，要么一条都不变。
        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            foreach (var file in files)
            {
                await TakeFileSpaceHoldAsync(file, ct);
                file.FolderId = folderId;
            }

            await _fileRepository.UpdateManyAsync(files, ct);
        });

        return Ok();
    }

    public async Task<Result<FileFolderDto>> EnsurePathAsync(Guid ownerId, string path, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(path);

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return Fail<FileFolderDto>("The folder path must name at least one folder, for example \"/Documents\"", 400);
        }

        if (segments.Any(segment => !FolderNameValidator.IsValid(segment)))
        {
            return Fail<FileFolderDto>(FolderNameValidator.InvalidNameMessage, 400);
        }

        FileFolder? parent = null;
        foreach (var segment in segments)
        {
            var segmentPath = BuildPath(segment, parent);
            var parentId = parent?.Id;

            var existing = await _folderRepository.FirstOrDefaultAsync(
                f => f.Path == segmentPath && f.CreatorId == ownerId && !f.IsDeleted, cancellationToken);
            if (existing != null)
            {
                parent = existing;
                continue;
            }

            var folder = new FileFolder
            {
                Name = segment,
                ParentId = parentId,
                Path = segmentPath,
                CreatorId = ownerId
            };

            try
            {
                await _folderRepository.InsertAsync(folder, cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
            {
                // 并发的另一次 Ensure 刚建了同一个目录（立即保存的仓储在这里就撞上唯一索引）：
                // 摘掉这一份，改用它建的那一份。在工作单元里时插入延到提交，撞上的是提交本身，这里接不住。
                _folderRepository.Discard(folder);
                folder = await _folderRepository.FirstOrDefaultAsync(
                    f => f.Path == segmentPath && f.CreatorId == ownerId && !f.IsDeleted, cancellationToken);
                if (folder == null)
                    throw;
            }

            parent = folder;

            if (EventBus != null)
            {
                await EventBus.PublishAsync(new FileFolderCreatedEvent
                {
                    FolderId = folder.Id,
                    Name = folder.Name,
                    ParentId = folder.ParentId,
                    Path = folder.Path
                }, cancellationToken);
            }
        }

        return Ok(await MapToDto(parent!));
    }

    #region Private Methods

    /// <summary>
    /// 归档进文件空间（挪进目录或挪回根目录）的文件由文件空间持有：临时上传（含被业务记录引用着的附件）转为正式记录，
    /// 只剩业务记录在持有的正式记录添一个隐式持有者。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 临时记录（<c>IsTemporary = true</c>）的生命周期只由 <c>[FileField]</c> 引用决定，计数等于正式引用行数，
    /// 过了保留期仍为 0 就被孤儿回收物理删除。文件库里的文件没有任何引用行：用户把一份直传的临时上传
    /// 挪进目录、建了分享、传了新版本，开启后台清理后它照样在保留期满时连同缩略图一起被删掉，分享链接 404，
    /// 而没有任何告警。挪动是用户在文件库里整理它的明确动作，于是在这里转正。
    /// </para>
    /// <para>
    /// 计数在既有的引用行之外加一个隐式持有者，与正式上传「从 1 起」同一语义（正式记录 = 隐式持有者 + 引用行）；
    /// 已经有隐式持有者的不再加，否则每挪一次目录，删除时就要多删一次才真的删得掉。
    /// 挪回根目录同样要持有：根目录里的文件就是靠这份持有被认作「在文件空间里」的（见 <see cref="FileSpaceQueries"/>）。
    /// </para>
    /// <para>
    /// ★ 正式记录也要这一份：上传者删掉了自己那一份、而一条消息还引用着的文件，计数恰好等于引用行数，没有隐式持有者。
    /// 把它放进文件空间之后，引用它的那条消息一删，计数归零、文件被物理删除，而它还在用户的目录里；
    /// 挪回根目录则它直接从文件空间消失。
    /// </para>
    /// <para>
    /// ★ 转正与 +1、添持有都是一条条件语句（<see cref="FileReferenceCounts.TryPromoteAsync"/> /
    /// <see cref="FileReferenceCounts.TryTakeImplicitHoldAsync"/>）：计数不随整行写回（那会与并发的绑定 / 解绑互相覆盖），
    /// 两次并发的挪动也只加一次。内存里的那份随之改成正式记录，紧接着的整行写回写下的是同一个状态。
    /// </para>
    /// </remarks>
    private async Task TakeFileSpaceHoldAsync(FileRecord file, CancellationToken cancellationToken)
    {
        if (file.IsTemporary)
        {
            await FileReferenceCounts.TryPromoteAsync(_fileRepository, file.Id, cancellationToken);
            file.IsTemporary = false;
            return;
        }

        await FileReferenceCounts.TryTakeImplicitHoldAsync(_fileRepository, _referenceRepository, file.Id, cancellationToken);
    }

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
        return BuildPath(name, parent);
    }

    /// <summary>父目录的路径 + 名字；没有父目录就挂在根上。</summary>
    internal static string BuildPath(string name, FileFolder? parent)
        => $"{parent?.Path ?? string.Empty}/{name.Trim()}";

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
    /// 按 <c>ParentId</c> 闭包逐层取出后代，并把每一条的新路径从父链重建出来（写回 <see cref="FileFolder.Path"/>，
    /// 但此时还**不落库**）。
    /// </summary>
    /// <remarks>
    /// 刻意不用「<c>Path</c> 以旧路径为前缀」这个判据：它把整张表里任何恰好长在这个前缀下的行都当成后代
    /// 改写掉 —— 而这样的行并不都属于这棵子树（分隔符校验落地之前建出来的存量行就是）。按父链重建还顺带
    /// 修正与父链不一致的脏路径。
    /// </remarks>
    private async Task<List<FileFolder>> PlanDescendantPathsAsync(FileFolder root, string rootNewPath)
    {
        var planned = new List<FileFolder>();
        var newPathById = new Dictionary<Guid, string> { [root.Id] = rootNewPath };
        var frontier = new List<Guid> { root.Id };

        while (frontier.Count > 0)
        {
            var parentIds = frontier;
            var level = await _folderRepository.AsQueryable()
                .Where(f => f.ParentId.HasValue && parentIds.Contains(f.ParentId.Value) && !f.IsDeleted)
                .ToListAsync();

            foreach (var child in level)
            {
                var parentPath = newPathById[child.ParentId!.Value];
                var childPath = $"{parentPath}/{child.Name}";
                newPathById[child.Id] = childPath;
                child.Path = childPath;
                planned.Add(child);
            }

            frontier = level.Select(f => f.Id).ToList();
        }

        return planned;
    }

    /// <summary>
    /// 计划中的后代新路径是否与子树之外的现存目录撞上。撞上就该以 409 回答，
    /// 而不是把它留给提交时的唯一索引（那是一个每次重试都复现的 500）。
    /// </summary>
    private async Task<bool> DescendantPathsCollideAsync(List<FileFolder> planned, Guid? ownerId)
    {
        if (planned.Count == 0)
            return false;

        var ids = planned.Select(f => f.Id).ToList();
        var paths = planned.Select(f => f.Path).ToList();
        return await _folderRepository.AnyAsync(
            f => paths.Contains(f.Path) && f.CreatorId == ownerId && !ids.Contains(f.Id) && !f.IsDeleted);
    }

    private async Task WriteDescendantPathsAsync(List<FileFolder> planned, CancellationToken cancellationToken)
    {
        if (planned.Count > 0)
        {
            await _folderRepository.UpdateManyAsync(planned, cancellationToken);
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
                OwnerId = child.CreatorId,
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
        var folderId = folder.Id;
        var fileCount = await FileSpaceQueries
            .NotInRecycleBin(_fileRepository.AsQueryable(), _recycleBinRepository.AsQueryable())
            .CountAsync(f => f.FolderId == folderId);

        return new FileFolderDto
        {
            Id = folder.Id,
            Name = folder.Name,
            ParentId = folder.ParentId,
            Path = folder.Path,
            SortOrder = folder.SortOrder,
            Description = folder.Description,
            OwnerId = folder.CreatorId,
            FileCount = fileCount
        };
    }

    #endregion

    public async Task<Result> ReorderAsync(IReadOnlyList<Guid> ids, Guid? parentId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(ids);

        // 排序范围是「同一个所有者、同一个父目录下的兄弟」：每个用户的根目录是各自的，按父目录一刀切会把所有人的
        // 根目录排进同一个序列。所有者从被排序的目录上取，它们必须同属一人。
        var idList = ids.ToList();
        var owners = await _folderRepository.AsQueryable()
            .Where(f => idList.Contains(f.Id))
            .Select(f => f.CreatorId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (owners.Count > 1)
            return Fail("Folders of different owners cannot be reordered together", 400);

        var ownerId = owners.Count == 1 ? owners[0] : null;
        var result = await ExecuteInUnitOfWorkAsync(
            ct => _folderRepository.ReorderAsync(ids, f => f.ParentId == parentId && f.CreatorId == ownerId, ct),
            cancellationToken);

        if (!result.Succeeded)
            return Result.Failure(result.Message ?? "Reorder failed.", result.Code ?? 400, result.ErrorCode);

        LogInformation("Reordered {Count} folder(s) under parent {ParentId}", result.Data, parentId);
        return Ok();
    }
}
