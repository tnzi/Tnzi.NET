namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// File folder service interface for virtual directory management
/// </summary>
public interface IFileFolderService
{
    /// <summary>
    /// Create a new folder
    /// </summary>
    Task<Result<FileFolderDto>> CreateAsync(CreateFileFolderDto input);

    /// <summary>
    /// Update an existing folder
    /// </summary>
    Task<Result<FileFolderDto>> UpdateAsync(Guid id, UpdateFileFolderDto input);

    /// <summary>
    /// Delete a folder (must have no children or files)
    /// </summary>
    Task<Result> DeleteAsync(Guid id);

    /// <summary>
    /// Get a folder by ID
    /// </summary>
    Task<Result<FileFolderDto>> GetAsync(Guid id);

    /// <summary>
    /// Get folder tree starting from the specified parent (recursive)
    /// </summary>
    Task<Result<List<FileFolderDto>>> GetTreeAsync(Guid? parentId = null);

    /// <summary>
    /// Move a folder to a new parent (updates paths for all descendants)
    /// </summary>
    Task<Result> MoveAsync(Guid id, Guid? newParentId);

    /// <summary>
    /// Batch move files to a folder
    /// </summary>
    Task<Result> MoveFilesToFolderAsync(List<Guid> fileIds, Guid? folderId);

    /// <summary>
    /// Reorder folders within one parent (drag-and-drop ordering).
    /// </summary>
    /// <param name="ids">Folder ids in their new order; may be just the visible page</param>
    /// <param name="parentId">Parent scope; null = root folders</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<Result> ReorderAsync(IReadOnlyList<Guid> ids, Guid? parentId = null, CancellationToken cancellationToken = default);
}
