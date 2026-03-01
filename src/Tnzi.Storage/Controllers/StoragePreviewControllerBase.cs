namespace Tnzi.Storage.Controllers;

/// <summary>
/// 存储预览控制器基类
/// 提供预览能力、预览 URL 等 API 的抽象基类
/// </summary>
[Route("files/preview")]  // 路由说明: 基类为 files 可重写
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
public abstract class StoragePreviewControllerBase : ApiControllerBase
{
    protected readonly IFilePreviewService FilePreviewService;
    protected readonly IFileStorageService FileStorageService;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="filePreviewService">文件预览服务</param>
    /// <param name="fileStorageService">文件存储服务</param>
    protected StoragePreviewControllerBase(IFilePreviewService filePreviewService, IFileStorageService fileStorageService)
    {
        FilePreviewService = Check.NotNull(filePreviewService);
        FileStorageService = Check.NotNull(fileStorageService);
    }

    /// <summary>
    /// 判断文件是否支持预览
    /// </summary>
    /// <param name="id">文件 ID</param>
    /// <returns>是否支持预览</returns>
    [HttpGet("{id:guid}/can-preview")]
    public virtual async Task<ApiResult<bool>> CanPreview(Guid id)
    {
        var recordResult = await FileStorageService.GetRecordAsync(id);
        if (!recordResult.Succeeded)
        {
            return ApiResult<bool>.Error(recordResult.Message ?? "File not found", recordResult.Code ?? 404, recordResult.ErrorCode);
        }

        var result = FilePreviewService.CanPreview(recordResult.Data!);
        return Ok(result);
    }

    /// <summary>
    /// 获取预览 URL
    /// </summary>
    /// <param name="id">文件 ID</param>
    /// <returns>预览 URL</returns>
    [HttpGet("{id:guid}/preview-url")]
    public virtual async Task<ApiResult<string>> GetPreviewUrl(Guid id)
    {
        var recordResult = await FileStorageService.GetRecordAsync(id);
        if (!recordResult.Succeeded)
        {
            return ApiResult<string>.Error(recordResult.Message ?? "File not found", recordResult.Code ?? 404, recordResult.ErrorCode);
        }

        var result = await FilePreviewService.GetPreviewUrlAsync(recordResult.Data!);
        return Ok<string>(result);
    }

    /// <summary>
    /// 获取预览类型
    /// </summary>
    /// <param name="id">文件 ID</param>
    /// <returns>类型如 image, pdf, video, audio, text</returns>
    [HttpGet("{id:guid}/preview-type")]
    public virtual async Task<ApiResult<string>> GetPreviewType(Guid id)
    {
        var recordResult = await FileStorageService.GetRecordAsync(id);
        if (!recordResult.Succeeded)
        {
            return ApiResult<string>.Error(recordResult.Message ?? "File not found", recordResult.Code ?? 404, recordResult.ErrorCode);
        }

        var result = FilePreviewService.GetPreviewType(recordResult.Data!);
        return Ok<string>(result);
    }

    /// <summary>
    /// 生成并返回预览流
    /// </summary>
    /// <param name="id">文件 ID</param>
    /// <returns>预览流</returns>
    [HttpGet("{id:guid}/preview")]
    public virtual async Task<IActionResult> GeneratePreview(Guid id)
    {
        var recordResult = await FileStorageService.GetRecordAsync(id);
        if (!recordResult.Succeeded)
        {
            return new NotFoundResult();
        }

        var record = recordResult.Data!;
        if (!FilePreviewService.CanPreview(record))
        {
            return new BadRequestObjectResult("File does not support preview");
        }

        var stream = await FilePreviewService.GeneratePreviewAsync(record);
        var contentType = FilePreviewService.GetPreviewType(record) switch
        {
            "image" => "image/png",
            "pdf" => "application/pdf",
            "video" => "video/mp4",
            "audio" => "audio/mpeg",
            "text" => "text/plain",
            _ => "application/octet-stream"
        };

        return File(stream, contentType);
    }
}

