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
    /// Get folder tree starting from the specified parent (recursive). Callers holding <c>storage.file.view</c>
    /// see every owner's folders and may narrow the tree to one owner; everyone else sees only their own.
    /// </summary>
    /// <param name="parentId">Start below this folder; null = from the root</param>
    /// <param name="ownerId">Only this owner's folders (administrators); for anyone else a different owner yields an empty tree</param>
    Task<Result<List<FileFolderDto>>> GetTreeAsync(Guid? parentId = null, Guid? ownerId = null);

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

    /// <summary>
    /// 保证 <paramref name="ownerId"/> 的文件空间里有 <paramref name="path"/> 这条目录（如 <c>/AI/Summaries</c>），
    /// 缺哪一级建哪一级，返回最末一级。已存在就原样返回，可以反复调用。
    /// </summary>
    /// <remarks>
    /// ★ <b>系统身份</b>的操作，不做授权判定：给消费方的服务端代码在用户注册、首次打开文件空间、某个应用第一次写文件时
    /// 替用户建默认目录用（聊天附件、AI 产出、每个应用一个目录）。<b>不要</b>把它接到一个由请求决定 <paramref name="ownerId"/>
    /// 的端点上 —— 那等于让调用方往任意用户的空间里建目录。路径里每一段都过与 <see cref="CreateAsync"/> 相同的名字检查。
    /// </remarks>
    Task<Result<FileFolderDto>> EnsurePathAsync(Guid ownerId, string path, CancellationToken cancellationToken = default);
}
