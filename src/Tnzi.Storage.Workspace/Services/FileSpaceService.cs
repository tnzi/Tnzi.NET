namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// <see cref="IFileSpaceService"/> 的默认实现。
/// </summary>
public class FileSpaceService : ApplicationService, IFileSpaceService
{
    private const string RootLocation = "/";

    private readonly IRepository<FileFolder, Guid> _folderRepository;
    private readonly IRepository<FileRecord, Guid> _fileRepository;
    private readonly IRepository<FileReference, Guid> _referenceRepository;
    private readonly IRepository<RecycleBinEntry, Guid> _recycleBinRepository;
    private readonly IFileStorageService _fileStorage;

    public FileSpaceService(
        IServiceProvider serviceProvider,
        IRepository<FileFolder, Guid> folderRepository,
        IRepository<FileRecord, Guid> fileRepository,
        IRepository<FileReference, Guid> referenceRepository,
        IRepository<RecycleBinEntry, Guid> recycleBinRepository,
        IFileStorageService fileStorage)
        : base(serviceProvider)
    {
        _folderRepository = Check.NotNull(folderRepository);
        _fileRepository = Check.NotNull(fileRepository);
        _referenceRepository = Check.NotNull(referenceRepository);
        _recycleBinRepository = Check.NotNull(recycleBinRepository);
        _fileStorage = Check.NotNull(fileStorage);
    }

    public async Task<Result<FileSpaceListingDto>> ListAsync(FileSpaceListRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (CurrentUser?.Id is not { } ownerId)
            return Fail<FileSpaceListingDto>("Sign in to browse your files", 401, ErrorCodes.UNAUTHORIZED);

        FileFolder? folder = null;
        if (request.FolderId is { } requestedFolderId)
        {
            folder = await FindOwnFolderAsync(ownerId, requestedFolderId, cancellationToken);
            if (folder == null)
                return Fail<FileSpaceListingDto>("Folder not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var folderId = folder?.Id;
        var folders = _folderRepository.AsQueryable().Where(f => f.CreatorId == ownerId && f.ParentId == folderId);
        var files = folderId is { } id
            ? FileSpaceQueries.InFolder(_fileRepository.AsQueryable(), ownerId, id, _recycleBinRepository.AsQueryable())
            : FileSpaceQueries.AtRoot(_fileRepository.AsQueryable(), ownerId, _referenceRepository.AsQueryable(), _recycleBinRepository.AsQueryable());

        var folderCount = await folders.CountAsync(cancellationToken);
        var fileCount = await files.CountAsync(cancellationToken);

        // 目录在前、文件在后，分页跨过两者的分界：先从目录里取这一页落在目录上的那一段，剩下的名额给文件。
        var skip = request.Skip;
        var take = request.Take;
        var entries = new List<FileSpaceEntryDto>(take);
        var location = folder?.Path ?? RootLocation;

        if (skip < folderCount)
        {
            var folderPage = await SortFolders(folders, request.SortBy, request.Descending)
                .Skip(skip)
                .Take(take)
                .ToListAsync(cancellationToken);
            entries.AddRange(folderPage.Select(f => ToEntry(f, location)));
        }

        var remaining = take - entries.Count;
        if (remaining > 0)
        {
            var fileSkip = Math.Max(0, skip - folderCount);
            var filePage = await SortFiles(files, request.SortBy, request.Descending)
                .Skip(fileSkip)
                .Take(remaining)
                .ToListAsync(cancellationToken);
            entries.AddRange(filePage.Select(f => ToEntry(f, location)));
        }

        return Ok(new FileSpaceListingDto
        {
            FolderId = folderId,
            Path = location,
            Breadcrumbs = folder == null ? [] : await BuildBreadcrumbsAsync(folder, cancellationToken),
            Entries = new PagedList<FileSpaceEntryDto>(entries, request.PageIndex, request.PageSize, folderCount + fileCount)
        });
    }

    public async Task<Result<IPagedList<FileSpaceEntryDto>>> SearchAsync(FileSpaceSearchRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (CurrentUser?.Id is not { } ownerId)
            return Fail<IPagedList<FileSpaceEntryDto>>("Sign in to search your files", 401, ErrorCodes.UNAUTHORIZED);

        var query = FileSpaceQueries.InSpace(
            _fileRepository.AsQueryable(),
            ownerId,
            _folderRepository.AsQueryable(),
            _referenceRepository.AsQueryable(),
            _recycleBinRepository.AsQueryable());

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLower();
            query = query.Where(f => f.OriginalName.ToLower().Contains(keyword));
        }

        if (!string.IsNullOrWhiteSpace(request.Extension))
        {
            var extension = request.Extension.Trim().ToLower();
            if (!extension.StartsWith('.'))
                extension = "." + extension;
            query = query.Where(f => f.Extension.ToLower() == extension);
        }

        if (!string.IsNullOrWhiteSpace(request.ContentType))
        {
            var contentType = request.ContentType.Trim().ToLower();
            query = query.Where(f => f.ContentType.ToLower().StartsWith(contentType));
        }

        var total = await query.CountAsync(cancellationToken);
        var page = await SortFiles(query, request.SortBy, request.Descending)
            .Skip(request.Skip)
            .Take(request.Take)
            .ToListAsync(cancellationToken);

        // 一页结果所在目录的路径一次查完：搜索结果要说清楚文件在哪。
        var folderIds = page.Where(f => f.FolderId != null).Select(f => f.FolderId!.Value).Distinct().ToList();
        var paths = folderIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _folderRepository.AsQueryable()
                .Where(f => folderIds.Contains(f.Id) && f.CreatorId == ownerId)
                .ToDictionaryAsync(f => f.Id, f => f.Path, cancellationToken);

        var entries = page
            .Select(f => ToEntry(f, f.FolderId is { } id && paths.TryGetValue(id, out var path) ? path : RootLocation))
            .ToList();

        return Ok<IPagedList<FileSpaceEntryDto>>(new PagedList<FileSpaceEntryDto>(entries, request.PageIndex, request.PageSize, total));
    }

    public async Task<Result<FileRecord>> UploadFileAsync(Guid? folderId, string fileName, Stream stream, CancellationToken cancellationToken = default)
    {
        if (CurrentUser?.Id is not { } ownerId)
            return Fail<FileRecord>("Sign in to upload files", 401, ErrorCodes.UNAUTHORIZED);

        return await SaveIntoSpaceAsync(ownerId, folderId, fileName, stream, explicitOwner: false, cancellationToken);
    }

    public Task<Result<FileRecord>> SaveForOwnerAsync(Guid ownerId, Guid? folderId, string fileName, Stream stream, CancellationToken cancellationToken = default)
    {
        Check.NotNull(stream);
        return SaveIntoSpaceAsync(ownerId, folderId, fileName, stream, explicitOwner: true, cancellationToken);
    }

    /// <summary>
    /// 存一份正式、不去重的记录，并在同一个工作单元里把它放进目标目录：要么记录与归档一起落库，要么都不落。
    /// </summary>
    private async Task<Result<FileRecord>> SaveIntoSpaceAsync(
        Guid ownerId, Guid? folderId, string fileName, Stream stream, bool explicitOwner, CancellationToken cancellationToken)
    {
        if (folderId is { } targetId && await FindOwnFolderAsync(ownerId, targetId, cancellationToken) == null)
            return Fail<FileRecord>("Folder not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var options = new FileSaveOptions
        {
            Deduplicate = false,
            // 当前用户自己上传时不必指定（父模块按当前用户落 CreatorId、算配额）；系统身份代写时必须指定。
            OwnerId = explicitOwner ? ownerId : null
        };

        return await ExecuteInUnitOfWorkAsync(async ct =>
        {
            var saved = await _fileStorage.SaveAsync(fileName, stream, options, ct);
            if (!saved.Succeeded || folderId == null)
                return saved;

            var record = saved.Data!;
            record.FolderId = folderId;
            await _fileRepository.UpdateAsync(record, ct);
            return saved;
        }, cancellationToken);
    }

    /// <summary>这个用户自己的、没被删（也不在回收站里）的目录；不是就返回 null。</summary>
    private async Task<FileFolder?> FindOwnFolderAsync(Guid ownerId, Guid folderId, CancellationToken cancellationToken)
    {
        var folder = await _folderRepository.GetAsync(folderId, cancellationToken);
        return folder != null && !folder.IsDeleted && folder.CreatorId == ownerId ? folder : null;
    }

    private async Task<List<FileSpaceBreadcrumbDto>> BuildBreadcrumbsAsync(FileFolder folder, CancellationToken cancellationToken)
    {
        var chain = new List<FileSpaceBreadcrumbDto>();
        var visited = new HashSet<Guid>();
        var current = folder;
        while (current != null && visited.Add(current.Id))
        {
            chain.Add(new FileSpaceBreadcrumbDto { Id = current.Id, Name = current.Name });
            current = current.ParentId is { } parentId
                ? await _folderRepository.GetAsync(parentId, cancellationToken)
                : null;
        }

        chain.Reverse();
        return chain;
    }

    private static IQueryable<FileFolder> SortFolders(IQueryable<FileFolder> folders, FileSpaceSortField sortBy, bool descending)
        => sortBy switch
        {
            FileSpaceSortField.Time => descending
                ? folders.OrderByDescending(f => f.CreationTime).ThenBy(f => f.Id)
                : folders.OrderBy(f => f.CreationTime).ThenBy(f => f.Id),
            // 目录没有大小：按大小排时目录之间按名字排，目录整体仍在文件前面。
            FileSpaceSortField.Size => folders.OrderBy(f => f.Name).ThenBy(f => f.Id),
            _ => descending
                ? folders.OrderByDescending(f => f.Name).ThenBy(f => f.Id)
                : folders.OrderBy(f => f.Name).ThenBy(f => f.Id)
        };

    private static IQueryable<FileRecord> SortFiles(IQueryable<FileRecord> files, FileSpaceSortField sortBy, bool descending)
        => sortBy switch
        {
            FileSpaceSortField.Time => descending
                ? files.OrderByDescending(f => f.CreationTime).ThenBy(f => f.Id)
                : files.OrderBy(f => f.CreationTime).ThenBy(f => f.Id),
            FileSpaceSortField.Size => descending
                ? files.OrderByDescending(f => f.Size).ThenBy(f => f.Id)
                : files.OrderBy(f => f.Size).ThenBy(f => f.Id),
            _ => descending
                ? files.OrderByDescending(f => f.OriginalName).ThenBy(f => f.Id)
                : files.OrderBy(f => f.OriginalName).ThenBy(f => f.Id)
        };

    private static FileSpaceEntryDto ToEntry(FileFolder folder, string location) => new()
    {
        Kind = FileSpaceEntryKind.Folder,
        Id = folder.Id,
        Name = folder.Name,
        ParentId = folder.ParentId,
        Location = location,
        CreationTime = folder.CreationTime
    };

    private static FileSpaceEntryDto ToEntry(FileRecord file, string location) => new()
    {
        Kind = FileSpaceEntryKind.File,
        Id = file.Id,
        Name = string.IsNullOrEmpty(file.OriginalName) ? file.FileName : file.OriginalName,
        ParentId = file.FolderId,
        Location = location,
        Size = file.Size,
        Extension = file.Extension,
        ContentType = file.ContentType,
        HasThumbnail = !string.IsNullOrEmpty(file.ThumbnailPath),
        IsPublic = file.IsPublic,
        CreationTime = file.CreationTime
    };
}
