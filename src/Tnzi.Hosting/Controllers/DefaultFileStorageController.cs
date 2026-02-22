using FileShare = Tnzi.Storage.Entities.FileShare;
using FileUploadSession = Tnzi.Storage.Entities.FileUploadSession;
using FileChunk = Tnzi.Storage.Entities.FileChunk;

namespace Tnzi.Hosting.Controllers;

/// <summary>
/// 默认文件存储控制器（用户端）
/// 路由：/api/files（自动添加 /api 前缀）
/// 只提供基础上传和读取功能，复杂功能通过Service调用
/// 上传需要登录认证，读取匿名可访问
/// </summary>
[ApiController]
[Route("files")]
[ApiExplorerSettings(GroupName = "user")]
[ApiAuthorize]
public class DefaultFileStorageController : StorageControllerBase
{
    public DefaultFileStorageController(
        IFileStorageService fileStorageService,
        IFileShareService fileShareService,
        IFileVersionService fileVersionService,
        IFileChunkUploadService fileChunkUploadService)
        : base(fileStorageService, fileShareService, fileVersionService, fileChunkUploadService)
    {
    }

    // 读取方法 - 匿名可访问
    [HttpGet("{id}")]
    [AllowAnonymous]
    public override async Task<ApiResult<FileRecord>> GetById(Guid id)
    {
        return await base.GetById(id);
    }

    [HttpGet("{id}/download")]
    [AllowAnonymous]
    public override async Task<IActionResult> Download(Guid id)
    {
        return await base.Download(id);
    }

    [HttpGet("{id}/thumbnail")]
    [AllowAnonymous]
    public override async Task<IActionResult> GetThumbnail(Guid id)
    {
        return await base.GetThumbnail(id);
    }

    [HttpGet("{id}/preview")]
    [AllowAnonymous]
    public override async Task<IActionResult> Preview(Guid id)
    {
        return await base.Preview(id);
    }

    // 复杂功能 - 隐藏API，通过Service调用
    [HttpDelete("{id}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult> Delete(Guid id)
    {
        return await base.Delete(id);
    }

    [HttpGet("{id}/url")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult<string>> GetUrl(Guid id, [FromQuery] int? expiresIn = null)
    {
        return await base.GetUrl(id, expiresIn);
    }

    [HttpPut("{id}/rename")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult<FileRecord>> Rename(Guid id, [FromBody] RenameFileRequest request)
    {
        return await base.Rename(id, request);
    }

    [HttpPost("{id}/copy")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult<FileRecord>> CopyFile(Guid id, [FromBody] CopyFileRequest? request = null)
    {
        return await base.CopyFile(id, request);
    }

    [HttpPost("{id}/versions")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult<FileVersion>> CreateVersion(
        Guid id,
        IFormFile file,
        [FromForm] string? description = null)
    {
        return await base.CreateVersion(id, file, description);
    }

    [HttpGet("{id}/versions")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult<IEnumerable<FileVersion>>> GetVersions(Guid id)
    {
        return await base.GetVersions(id);
    }

    [HttpPost("{id}/versions/{version}/restore")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult<FileRecord>> RestoreVersion(Guid id, int version)
    {
        return await base.RestoreVersion(id, version);
    }

    [HttpPost("{id}/share")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult<FileShare>> CreateShare(Guid id, [FromBody] CreateShareRequest request)
    {
        return await base.CreateShare(id, request);
    }

    [HttpGet("share/{token}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult<FileShare>> GetShare(string token)
    {
        return await base.GetShare(token);
    }

    [HttpDelete("share/{token}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult> RevokeShare(string token)
    {
        return await base.RevokeShare(token);
    }

    [HttpGet("share/{token}/download")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<IActionResult> DownloadByShareToken(string token, [FromQuery] string? password = null)
    {
        return await base.DownloadByShareToken(token, password);
    }

    [HttpPost("compress")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult<FileRecord>> Compress([FromBody] CompressRequest request)
    {
        return await base.Compress(request);
    }

    [HttpPost("{id}/decompress")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult<IEnumerable<FileRecord>>> Decompress(Guid id)
    {
        return await base.Decompress(id);
    }

    [HttpPost("upload/chunk/init")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult<FileUploadSession>> InitiateChunkedUpload([FromBody] InitiateChunkedUploadRequest request)
    {
        return await base.InitiateChunkedUpload(request);
    }

    [HttpPost("upload/chunk/{uploadSessionId}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult<FileChunk>> UploadChunk(
        Guid uploadSessionId,
        [FromQuery] int chunkIndex,
        IFormFile chunk)
    {
        return await base.UploadChunk(uploadSessionId, chunkIndex, chunk);
    }

    [HttpPost("upload/chunk/{uploadSessionId}/complete")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult<FileRecord>> CompleteChunkedUpload(
        Guid uploadSessionId,
        [FromBody] CompleteChunkedUploadRequest? request = null)
    {
        return await base.CompleteChunkedUpload(uploadSessionId, request);
    }

    [HttpDelete("upload/chunk/{uploadSessionId}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult> CancelChunkedUpload(Guid uploadSessionId)
    {
        return await base.CancelChunkedUpload(uploadSessionId);
    }

    [HttpGet("upload/chunk/{uploadSessionId}/progress")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public override async Task<ApiResult<FileUploadProgress>> GetUploadProgress(Guid uploadSessionId)
    {
        return await base.GetUploadProgress(uploadSessionId);
    }
}
