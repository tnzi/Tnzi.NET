namespace Tnzi.Storage.Workspace.Controllers;

/// <summary>
/// 当前用户的回收站：把文件与目录删进来、列出、还原、永久删除、清空。
/// </summary>
/// <remarks>
/// 裸 <c>[ApiAuthorize]</c>（登录即可）：判定在服务层。删除进的是<b>所有者</b>的回收站；列出、还原、永久删除只对自己的回收站。
/// 过期条目由后台清理按 <c>Storage:RecycleBin:RetentionDays</c> 自动清除（需开启 <c>Storage:Cleanup:Enabled</c>）。
/// </remarks>
[DefaultController]
[Route("storage/recycle-bin")]
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "storage")]
public class DefaultRecycleBinController : ApiControllerBase
{
    protected readonly IRecycleBinService RecycleBinService;

    public DefaultRecycleBinController(IRecycleBinService recycleBinService)
    {
        RecycleBinService = Check.NotNull(recycleBinService);
    }

    /// <summary>
    /// List the signed-in user's recycle bin, most recently deleted first
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<RecycleBinEntryDto>>> GetEntries([FromQuery] RecycleBinQueryDto query)
    {
        var result = await RecycleBinService.GetEntriesAsync(query, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// Move files and folders (with everything in them) to the recycle bin
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult> MoveToRecycleBin([FromBody] MoveToRecycleBinRequest request)
    {
        var result = await RecycleBinService.MoveToRecycleBinAsync(request?.FileIds, request?.FolderIds, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// Restore items to where they were deleted from (or to the root when that folder is gone)
    /// </summary>
    [HttpPost("restore")]
    public virtual async Task<ApiResult<RecycleBinRestoreResult>> Restore([FromBody] RecycleBinEntriesRequest request)
    {
        var result = await RecycleBinService.RestoreAsync(request.EntryIds, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// Delete items permanently. Files still referenced elsewhere (for example by a chat message) are kept for
    /// those references and only leave the file space
    /// </summary>
    [HttpPost("purge")]
    public virtual async Task<ApiResult<int>> Purge([FromBody] RecycleBinEntriesRequest request)
    {
        var result = await RecycleBinService.PurgeAsync(request.EntryIds, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// Empty the signed-in user's recycle bin
    /// </summary>
    [HttpDelete]
    public virtual async Task<ApiResult<int>> EmptyRecycleBin()
    {
        var result = await RecycleBinService.EmptyAsync(HttpContext.RequestAborted);
        return result.ToApiResult();
    }
}
