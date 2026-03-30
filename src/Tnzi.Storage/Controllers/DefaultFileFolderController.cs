namespace Tnzi.Storage.Controllers;

/// <summary>
/// Default file folder controller for virtual directory management
/// </summary>
[DefaultController]
[Route("storage/folders")]
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "storage")]
public class DefaultFileFolderController : ApiControllerBase
{
    protected readonly IFileFolderService FileFolderService;

    public DefaultFileFolderController(IFileFolderService fileFolderService)
    {
        FileFolderService = Check.NotNull(fileFolderService);
    }

    /// <summary>
    /// Get a folder by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<FileFolderDto>> Get(Guid id)
    {
        var result = await FileFolderService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// Get folder tree (recursive)
    /// </summary>
    [HttpGet("tree")]
    public virtual async Task<ApiResult<List<FileFolderDto>>> GetTree([FromQuery] Guid? parentId = null)
    {
        var result = await FileFolderService.GetTreeAsync(parentId);
        return result.ToApiResult();
    }

    /// <summary>
    /// Create a new folder
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<FileFolderDto>> Create([FromBody] CreateFileFolderDto input)
    {
        var result = await FileFolderService.CreateAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// Update an existing folder
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<FileFolderDto>> Update(Guid id, [FromBody] UpdateFileFolderDto input)
    {
        var result = await FileFolderService.UpdateAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// Delete a folder
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await FileFolderService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// Move a folder to a new parent
    /// </summary>
    [HttpPost("{id:guid}/move")]
    public virtual async Task<ApiResult> Move(Guid id, [FromQuery] Guid? newParentId = null)
    {
        var result = await FileFolderService.MoveAsync(id, newParentId);
        return result.ToApiResult();
    }

    /// <summary>
    /// Move files to a folder
    /// </summary>
    [HttpPost("move-files")]
    public virtual async Task<ApiResult> MoveFiles([FromBody] MoveFilesToFolderRequest request)
    {
        var result = await FileFolderService.MoveFilesToFolderAsync(request.FileIds, request.FolderId);
        return result.ToApiResult();
    }
}
