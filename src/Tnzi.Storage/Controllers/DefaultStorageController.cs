namespace Tnzi.Storage.Controllers;

/// <summary>
/// 存储控制器基类
/// 提供文件上传、下载、版本、分享、分块上传等 API 的抽象基类
/// </summary>
[DefaultController]
[Route("files")]
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultStorageController : ApiControllerBase
{
    protected readonly IFileStorageService FileStorageService;
    protected readonly IFileShareService FileShareService;
    protected readonly IFileVersionService FileVersionService;
    protected readonly IFileChunkUploadService FileChunkUploadService;

    /// <summary>
    /// 构造函数
    /// </summary>
    public DefaultStorageController(
        IFileStorageService fileStorageService,
        IFileShareService fileShareService,
        IFileVersionService fileVersionService,
        IFileChunkUploadService fileChunkUploadService)
    {
        FileStorageService = Check.NotNull(fileStorageService);
        FileShareService = Check.NotNull(fileShareService);
        FileVersionService = Check.NotNull(fileVersionService);
        FileChunkUploadService = Check.NotNull(fileChunkUploadService);
    }

    /// <summary>
    /// 根据 ID 获取文件
    /// </summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public virtual async Task<ApiResult<FileRecordDto>> GetById(Guid id)
    {
        var result = await FileStorageService.GetRecordAsync(id);
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// 删除文件
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await FileStorageService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 上传文件
    /// </summary>
    [HttpPost("upload")]
    public virtual async Task<ApiResult<FileRecordDto>> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest<FileRecordDto>("File is required");
        }

        using var stream = file.OpenReadStream();
        var result = await FileStorageService.SaveAsync(file.FileName, stream, isTemporary: true);
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// 批量上传
    /// </summary>
    [HttpPost("upload/batch")]
    public virtual async Task<ApiResult<IEnumerable<FileRecordDto>>> UploadMany(IEnumerable<IFormFile> files)
    {
        if (files == null || !files.Any())
        {
            return BadRequest<IEnumerable<FileRecordDto>>("Files are required");
        }

        var fileList = new List<(string fileName, Stream stream)>();
        var streams = new List<Stream>();

        try
        {
            foreach (var file in files)
            {
                var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                streams.Add(memoryStream);
                fileList.Add((file.FileName, memoryStream));
            }

            var result = await FileStorageService.SaveManyAsync(fileList);
            return result.Map(items => items.Select(r => r.MapTo<FileRecordDto>())).ToApiResult();
        }
        finally
        {
            foreach (var stream in streams)
            {
                stream.Dispose();
            }
        }
    }

    /// <summary>
    /// 根据 ID 下载文件
    /// </summary>
    [HttpGet("{id:guid}/download")]
    [AllowAnonymous]
    public virtual async Task<IActionResult> Download(Guid id)
    {
        var recordResult = await FileStorageService.GetRecordAsync(id);
        if (!recordResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var record = recordResult.Data;

        // 处理 Range 请求
        var rangeHeader = Request.Headers.Range.FirstOrDefault();
        if (!string.IsNullOrEmpty(rangeHeader))
        {
            var (rangeStart, rangeEnd) = ParseRangeHeader(rangeHeader, record!.Size);
            if (rangeStart.HasValue)
            {
                var rangeResult = await FileStorageService.GetRangeAsync(id, rangeStart, rangeEnd);
                if (!rangeResult.Succeeded)
                {
                    return new NotFoundResult();
                }
                var (stream, start, end, totalLength) = rangeResult.Data!;

                Response.Headers.ContentRange = $"bytes {start}-{end}/{totalLength}";
                Response.Headers.AcceptRanges = "bytes";
                Response.StatusCode = 206;
                Response.ContentLength = end - start + 1;

                return File(stream, record.ContentType ?? "application/octet-stream",
                    record.OriginalName ?? record.FileName,
                    enableRangeProcessing: true);
            }
        }

        var fullStreamResult = await FileStorageService.GetAsync(id);
        if (!fullStreamResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var fullStream = fullStreamResult.Data!;
        Response.Headers.AcceptRanges = "bytes";
        Response.ContentLength = record!.Size;
        return File(fullStream, record!.ContentType ?? "application/octet-stream",
            record!.OriginalName ?? record!.FileName);
    }

    /// <summary>
    /// 根据 ID 获取缩略图
    /// </summary>
    [HttpGet("{id:guid}/thumbnail")]
    [AllowAnonymous]
    public virtual async Task<IActionResult> GetThumbnail(Guid id)
    {
        var recordResult = await FileStorageService.GetRecordAsync(id);
        if (!recordResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var record = recordResult.Data!;

        var thumbnailResult = await FileStorageService.GetThumbnailAsync(id);
        if (!thumbnailResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var thumbnailStream = thumbnailResult.Data!;

        return File(thumbnailStream, "image/jpeg", $"thumb_{record.FileName}");
    }

    /// <summary>
    /// 根据 ID 预览文件
    /// </summary>
    [HttpGet("{id:guid}/preview")]
    [AllowAnonymous]
    public virtual async Task<IActionResult> Preview(Guid id)
    {
        var recordResult = await FileStorageService.GetRecordAsync(id);
        if (!recordResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var record = recordResult.Data;

        var streamResult = await FileStorageService.GetAsync(id);
        if (!streamResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var stream = streamResult.Data!;

        Response.Headers.CacheControl = "public, max-age=31536000";
        return File(stream, record!.ContentType ?? "application/octet-stream");
    }

    /// <summary>
    /// 获取文件访问 URL
    /// </summary>
    [HttpGet("{id:guid}/url")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<string>> GetUrl(Guid id, [FromQuery] int? expiresIn = null)
    {
        var result = await FileStorageService.GetUrlAsync(id, expiresIn);
        return result.ToApiResult();
    }

    /// <summary>
    /// Generate a presigned URL for temporary public access
    /// </summary>
    [HttpGet("{id:guid}/presigned-url")]
    public virtual async Task<ApiResult<string>> GetPresignedUrl(Guid id, [FromQuery] int expiresInSeconds = 3600)
    {
        var result = await FileStorageService.GetPresignedUrlAsync(id, expiresInSeconds, "GET");
        return result.ToApiResult();
    }

    /// <summary>
    /// 重命名文件
    /// </summary>
    [HttpPut("{id:guid}/rename")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileRecordDto>> Rename(Guid id, [FromBody] RenameFileRequest request)
    {
        var result = await FileStorageService.RenameAsync(id, request.NewFileName);
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// 复制文件
    /// </summary>
    [HttpPost("{id:guid}/copy")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileRecordDto>> CopyFile(Guid id, [FromBody] CopyFileRequest? request = null)
    {
        var newFileName = request?.NewFileName;
        var result = await FileStorageService.CopyAsync(id, newFileName);
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// 创建文件版本
    /// </summary>
    [HttpPost("{id:guid}/versions")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileVersionDto>> CreateVersion(
        Guid id,
        IFormFile file,
        [FromForm] string? description = null)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest<FileVersionDto>("File is required");
        }

        using var stream = file.OpenReadStream();
        var result = await FileVersionService.CreateVersionAsync(id, stream, description);
        // 控制器边界：把 FileVersion 实体投影为安全 DTO，绝不把内部字段（Path / Md5Hash / TenantId）泄漏进 API 契约。
        return result.Map(v => v.MapTo<FileVersionDto>()).ToApiResult();
    }

    /// <summary>
    /// 获取文件版本列表
    /// </summary>
    [HttpGet("{id:guid}/versions")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<IEnumerable<FileVersionDto>>> GetVersions(Guid id)
    {
        var result = await FileVersionService.GetVersionsAsync(id);
        // 控制器边界：把 FileVersion 实体投影为安全 DTO，绝不把内部字段（Path / Md5Hash / TenantId）泄漏进 API 契约。
        return result.Map(items => items.Select(v => v.MapTo<FileVersionDto>())).ToApiResult();
    }

    /// <summary>
    /// 恢复指定版本
    /// </summary>
    [HttpPost("{id:guid}/versions/{version}/restore")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileRecordDto>> RestoreVersion(Guid id, int version)
    {
        var result = await FileVersionService.RestoreVersionAsync(id, version);
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// 只读下载指定历史版本（不改变当前版本指针）
    /// </summary>
    [HttpGet("{id:guid}/versions/{version}/download")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<IActionResult> DownloadVersion(Guid id, int version)
    {
        var recordResult = await FileStorageService.GetRecordAsync(id);
        if (!recordResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var record = recordResult.Data!;

        var contentResult = await FileVersionService.GetVersionContentAsync(id, version);
        if (!contentResult.Succeeded)
        {
            return new NotFoundResult();
        }

        var contentType = record.ContentType ?? "application/octet-stream";
        var fileName = $"{record.OriginalName ?? record.FileName}.v{version}";
        return File(contentResult.Data!, contentType, fileName);
    }

    /// <summary>
    /// 删除指定历史版本（禁止删除当前版本）
    /// </summary>
    [HttpDelete("{id:guid}/versions/{version}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult> DeleteVersion(Guid id, int version)
    {
        var result = await FileVersionService.DeleteVersionAsync(id, version);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建分享链接
    /// </summary>
    [HttpPost("{id:guid}/share")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileSharePublicDto>> CreateShare(Guid id, [FromBody] CreateShareRequest request)
    {
        var result = await FileShareService.CreateShareAsync(
            id,
            request.ExpiresAt,
            request.MaxAccessCount,
            request.Password);
        // 投影到公开 DTO，绝不向外暴露 PasswordHash
        return result.Map(MapToPublicDto).ToApiResult();
    }

    /// <summary>
    /// 获取分享信息
    /// </summary>
    [HttpGet("share/{token}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileSharePublicDto>> GetShare(string token)
    {
        var result = await FileShareService.GetShareAsync(token);
        // 投影到公开 DTO，绝不向外暴露 PasswordHash
        return result.Map(MapToPublicDto).ToApiResult();
    }

    /// <summary>
    /// 撤销分享
    /// </summary>
    [HttpDelete("share/{token}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult> RevokeShare(string token)
    {
        var result = await FileShareService.RevokeShareAsync(token);
        return result.ToApiResult();
    }

    /// <summary>
    /// 通过分享 token 下载
    /// </summary>
    [HttpGet("share/{token}/download")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<IActionResult> DownloadByShareToken(string token, [FromQuery] string? password = null)
    {
        // 1) 校验密码/过期/启用（密码校验仍需在此进行）
        var isValidResult = await FileShareService.ValidateShareAccessAsync(token, password);
        if (!isValidResult.Succeeded || !isValidResult.Data)
        {
            return new UnauthorizedResult();
        }

        var shareResult = await FileShareService.GetShareAsync(token);
        if (!shareResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var share = shareResult.Data!;

        // 2) 原子占用一次配额：把"判超限 + 占额"合并为单次原子 DB 操作，消除竞态。
        //    返回 false 表示已超限/已禁用 → 拒绝下载（410 Gone）。
        var consumeResult = await FileShareService.IncrementShareAccessCountAsync(token);
        if (!consumeResult.Succeeded || !consumeResult.Data)
        {
            return new StatusCodeResult(StatusCodes.Status410Gone);
        }

        var fileRecordResult = await FileStorageService.GetRecordAsync(share.FileId);
        if (!fileRecordResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var fileRecord = fileRecordResult.Data!;

        var streamResult = await FileStorageService.GetAsync(share.FileId);
        if (!streamResult.Succeeded)
        {
            return new NotFoundResult();
        }
        var stream = streamResult.Data!;
        return File(stream, fileRecord.ContentType ?? "application/octet-stream", fileRecord.OriginalName ?? fileRecord.FileName);
    }

    /// <summary>
    /// 压缩文件
    /// </summary>
    [HttpPost("compress")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileRecordDto>> Compress([FromBody] CompressRequest request)
    {
        var result = await FileStorageService.CompressAsync(request.FileIds, request.ZipFileName);
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// 解压文件
    /// </summary>
    [HttpPost("{id:guid}/decompress")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<IEnumerable<FileRecordDto>>> Decompress(Guid id)
    {
        var result = await FileStorageService.DecompressAsync(id);
        return result.Map(items => items.Select(r => r.MapTo<FileRecordDto>())).ToApiResult();
    }

    /// <summary>
    /// 初始化分块上传
    /// </summary>
    [HttpPost("upload/chunk/init")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileUploadSession>> InitiateChunkedUpload([FromBody] InitiateChunkedUploadRequest request)
    {
        var result = await FileChunkUploadService.InitiateChunkedUploadAsync(
            request.FileName,
            request.TotalSize,
            request.ChunkSize,
            request.Md5Hash);
        return result.ToApiResult();
    }

    /// <summary>
    /// 上传分块
    /// </summary>
    [HttpPost("upload/chunk/{uploadSessionId:guid}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileChunk>> UploadChunk(
        Guid uploadSessionId,
        [FromQuery] int chunkIndex,
        IFormFile chunk)
    {
        if (chunk == null || chunk.Length == 0)
        {
            return BadRequest<FileChunk>("Chunk file is required");
        }

        using var stream = chunk.OpenReadStream();
        var result = await FileChunkUploadService.UploadChunkAsync(uploadSessionId, chunkIndex, stream);
        return result.ToApiResult();
    }

    /// <summary>
    /// 完成分块上传
    /// </summary>
    [HttpPost("upload/chunk/{uploadSessionId:guid}/complete")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileRecordDto>> CompleteChunkedUpload(
        Guid uploadSessionId,
        [FromBody] CompleteChunkedUploadRequest? request = null)
    {
        var result = await FileChunkUploadService.CompleteChunkedUploadAsync(
            uploadSessionId,
            request?.IsTemporary ?? false);
        return result.Map(r => r.MapTo<FileRecordDto>()).ToApiResult();
    }

    /// <summary>
    /// 取消分块上传
    /// </summary>
    [HttpDelete("upload/chunk/{uploadSessionId:guid}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult> CancelChunkedUpload(Guid uploadSessionId)
    {
        var result = await FileChunkUploadService.CancelChunkedUploadAsync(uploadSessionId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取上传进度
    /// </summary>
    [HttpGet("upload/chunk/{uploadSessionId:guid}/progress")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public virtual async Task<ApiResult<FileUploadProgress>> GetUploadProgress(Guid uploadSessionId)
    {
        var result = await FileChunkUploadService.GetUploadProgressAsync(uploadSessionId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 解析 Range 头
    /// </summary>
    private static (long? Start, long? End) ParseRangeHeader(string rangeHeader, long fileSize)
    {
        if (string.IsNullOrEmpty(rangeHeader) || !rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        var rangeValue = rangeHeader[6..];
        var parts = rangeValue.Split('-');

        if (parts.Length != 2)
        {
            return (null, null);
        }

        long? start = null;
        long? end = null;

        if (!string.IsNullOrEmpty(parts[0]))
        {
            if (long.TryParse(parts[0], out var startValue))
            {
                start = Math.Max(0, startValue);
            }
        }

        if (!string.IsNullOrEmpty(parts[1]))
        {
            if (long.TryParse(parts[1], out var endValue))
            {
                end = Math.Min(fileSize - 1, endValue);
            }
        }
        else if (start.HasValue)
        {
            end = fileSize - 1;
        }

        if (start.HasValue && end.HasValue && start.Value > end.Value)
        {
            return (null, null);
        }

        return (start, end);
    }

    /// <summary>
    /// 把 FileShare 实体投影为对外公开 DTO（绝不包含 PasswordHash）
    /// </summary>
    private static FileSharePublicDto MapToPublicDto(Tnzi.Storage.Entities.FileShare share)
    {
        return new FileSharePublicDto
        {
            Id = share.Id,
            FileId = share.FileId,
            ShareToken = share.ShareToken,
            ExpiresAt = share.ExpiresAt,
            MaxAccessCount = share.MaxAccessCount,
            AccessCount = share.AccessCount,
            RequirePassword = share.RequirePassword,
            IsEnabled = share.IsEnabled,
            CreationTime = share.CreationTime
        };
    }
}
