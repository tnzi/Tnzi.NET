namespace Tnzi.Storage.Workspace.Controllers;

/// <summary>
/// 当前用户自己的文件空间：浏览目录、搜索文件、把文件传进某个目录。
/// </summary>
/// <remarks>
/// <para>
/// 裸 <c>[ApiAuthorize]</c>（登录即可）：判定在服务层，每个用户只看得到、只写得进自己的文件空间。
/// 目录的增删改挪在 <c>storage/folders</c>，删除进回收站在 <c>storage/recycle-bin</c>，
/// 下载 / 预览 / 缩略图 / 版本 / 分享仍用 <c>files/{id}/...</c>（拿这里返回的文件 id）。
/// </para>
/// <para>
/// 大文件走分片上传：<c>POST files/upload/chunk/init</c> → 逐片上传 → <c>POST files/upload/chunk/{id}/complete</c>
/// 带 <c>{ "folderId": ... }</c>，合并出的文件直接落进目录，与这里的单次上传同一种记录（正式、不去重、计入配额）。
/// </para>
/// </remarks>
[DefaultController]
[Route("storage/space")]
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "storage")]
public class DefaultFileSpaceController : ApiControllerBase
{
    protected readonly IFileSpaceService FileSpaceService;

    public DefaultFileSpaceController(IFileSpaceService fileSpaceService)
    {
        FileSpaceService = Check.NotNull(fileSpaceService);
    }

    /// <summary>
    /// List a folder of the signed-in user's file space (omit folderId for the root): folders first, then files,
    /// paged together and sorted by name, time or size
    /// </summary>
    [HttpGet("items")]
    public virtual async Task<ApiResult<FileSpaceListingDto>> List([FromQuery] FileSpaceListRequest request)
    {
        var result = await FileSpaceService.ListAsync(request, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// Search the signed-in user's files by name, extension or content type (files in the recycle bin are left out)
    /// </summary>
    [HttpGet("search")]
    public virtual async Task<ApiResult<IPagedList<FileSpaceEntryDto>>> Search([FromQuery] FileSpaceSearchRequest request)
    {
        var result = await FileSpaceService.SearchAsync(request, HttpContext.RequestAborted);
        return result.ToApiResult();
    }

    /// <summary>
    /// Upload a file into one of the signed-in user's folders (omit folderId for the root). The file is kept
    /// permanently, is never merged with an earlier upload of the same bytes, and counts towards the user's quota
    /// </summary>
    [HttpPost("files")]
    [StorageUploadLimit]
    public virtual async Task<ApiResult<FileRecordDto>> Upload(IFormFile file, [FromForm] Guid? folderId = null)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest<FileRecordDto>("File is required");
        }

        await using var stream = file.OpenReadStream();
        var result = await FileSpaceService.UploadFileAsync(folderId, file.FileName, stream, HttpContext.RequestAborted);
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }
}
