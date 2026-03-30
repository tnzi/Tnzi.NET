namespace Tnzi.Storage.Services;

/// <summary>
/// File folder service implementation for virtual directory management
/// </summary>
public class FileFolderService : ApplicationService, IFileFolderService
{
    private readonly IRepository<FileFolder, Guid> _folderRepository;
    private readonly IRepository<FileRecord, Guid> _fileRepository;

    public FileFolderService(
        IServiceProvider serviceProvider,
        IRepository<FileFolder, Guid> folderRepository,
        IRepository<FileRecord, Guid> fileRepository)
        : base(serviceProvider)
    {
        _folderRepository = Check.NotNull(folderRepository);
        _fileRepository = Check.NotNull(fileRepository);
    }

    public async Task<Result<FileFolderDto>> CreateAsync(CreateFileFolderDto input)
    {
        Check.NotNull(input);
        Check.NotNullOrWhiteSpace(input.Name);

        // Validate parent exists if specified
        if (input.ParentId.HasValue)
        {
            var parent = await _folderRepository.GetAsync(input.ParentId.Value);
            if (parent == null)
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

        await EventBus?.PublishAsync(new FileFolderCreatedEvent
        {
            FolderId = folder.Id,
            Name = folder.Name,
            ParentId = folder.ParentId,
            Path = folder.Path
        })!;

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

        var nameChanged = false;

        if (input.Name != null)
        {
            Check.NotNullOrWhiteSpace(input.Name);
            var trimmedName = input.Name.Trim();
            if (trimmedName != folder.Name)
            {
                nameChanged = true;
                folder.Name = trimmedName;
            }
        }

        if (input.Description != null)
        {
            folder.Description = input.Description;
        }

        if (input.SortOrder.HasValue)
        {
            folder.SortOrder = input.SortOrder.Value;
        }

        // Rebuild path if name changed
        if (nameChanged)
        {
            var oldPath = folder.Path;
            folder.Path = await BuildPathAsync(folder.Name, folder.ParentId);

            // Check for duplicate path
            var exists = await _folderRepository.AnyAsync(
                f => f.Path == folder.Path && f.Id != id && !f.IsDeleted);
            if (exists)
            {
                return Fail<FileFolderDto>("A folder with the same name already exists at this location", 409);
            }

            // Update descendant paths
            await UpdateDescendantPathsAsync(oldPath, folder.Path);
        }

        await _folderRepository.UpdateAsync(folder);

        return Ok(await MapToDto(folder));
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var folder = await _folderRepository.GetAsync(id);
        if (folder == null)
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

        await EventBus?.PublishAsync(new FileFolderDeletedEvent
        {
            FolderId = folder.Id,
            Name = folder.Name,
            Path = folder.Path
        })!;

        return Ok();
    }

    public async Task<Result<FileFolderDto>> GetAsync(Guid id)
    {
        var folder = await _folderRepository.GetAsync(id);
        if (folder == null)
        {
            return Fail<FileFolderDto>("Folder not found", 404);
        }

        return Ok(await MapToDto(folder));
    }

    public async Task<Result<List<FileFolderDto>>> GetTreeAsync(Guid? parentId = null)
    {
        // Load all non-deleted folders to build tree in memory
        var allFolders = await _folderRepository.AsQueryable()
            .Where(f => !f.IsDeleted)
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

        // Cannot move to itself
        if (newParentId == id)
        {
            return Fail("Cannot move a folder into itself", 400);
        }

        // Validate new parent exists
        if (newParentId.HasValue)
        {
            var newParent = await _folderRepository.GetAsync(newParentId.Value);
            if (newParent == null)
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

        folder.ParentId = newParentId;
        folder.Path = await BuildPathAsync(folder.Name, newParentId);

        // Check for duplicate path at new location
        var exists = await _folderRepository.AnyAsync(
            f => f.Path == folder.Path && f.Id != id && !f.IsDeleted);
        if (exists)
        {
            return Fail("A folder with the same name already exists at the target location", 409);
        }

        // Update descendant paths
        await UpdateDescendantPathsAsync(oldPath, folder.Path);

        await _folderRepository.UpdateAsync(folder);

        await EventBus?.PublishAsync(new FileFolderMovedEvent
        {
            FolderId = folder.Id,
            Name = folder.Name,
            OldParentId = oldParentId,
            NewParentId = newParentId,
            OldPath = oldPath,
            NewPath = folder.Path
        })!;

        return Ok();
    }

    public async Task<Result> MoveFilesToFolderAsync(List<Guid> fileIds, Guid? folderId)
    {
        Check.NotNullOrEmpty(fileIds);

        // Validate target folder exists
        if (folderId.HasValue)
        {
            var folder = await _folderRepository.GetAsync(folderId.Value);
            if (folder == null)
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

        foreach (var file in files)
        {
            file.FolderId = folderId;
        }

        await _fileRepository.UpdateManyAsync(files);

        return Ok();
    }

    #region Private Methods

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
}
