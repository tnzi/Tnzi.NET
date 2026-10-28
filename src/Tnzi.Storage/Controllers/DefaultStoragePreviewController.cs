namespace Tnzi.Storage.Controllers;

/// <summary>
/// 存储预览控制器基类
/// 提供预览能力、预览 URL 等 API 的抽象基类
/// </summary>
[Route("files/preview")]  // 路由说明: 基类为 files 可重写
[ApiAuthorize]
[ApiExplorerSettings(GroupName = "user")]
[DefaultController]
public class DefaultStoragePreviewController : ApiControllerBase
{
    protected readonly IFilePreviewService FilePreviewService;
    protected readonly IFileStorageService FileStorageService;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="filePreviewService">文件预览服务</param>
    /// <param name="fileStorageService">文件存储服务</param>
    public DefaultStoragePreviewController(IFilePreviewService filePreviewService, IFileStorageService fileStorageService)
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
        Response.Headers.XContentTypeOptions = "nosniff";

        switch (FilePreviewService.GetPreviewType(record))
        {
            case "pdf":
                // 原件就是 PDF，或 Office 文档转换出来的 PDF。
                return File(stream, "application/pdf");
            case "text":
                // .html / .js / .json 在这里是被阅读的文字，不是被执行的文档：一律按纯文本发出。
                return File(stream, "text/plain");
            default:
                return OriginalBytes(record, stream);
        }
    }

    /// <summary>
    /// 原样返回的图片 / 视频 / 音频：声明记录自己的类型，形态与 <c>files/{id}/preview</c> 相同。
    /// </summary>
    /// <remarks>
    /// 此前按预览种类写死 <c>image/png</c> / <c>video/mp4</c> / <c>audio/mpeg</c>：SVG 被声明成 PNG，浏览器不会把它
    /// 嗅探回 SVG，<c>&lt;img&gt;</c> 与直接打开都是破图；<c>.webm</c> / <c>.wav</c> 也被报成别的格式。
    /// 内联白名单（<see cref="FileTypeHelper.IsInlineRenderable"/>）之外的类型（SVG）按附件发出、保留声明的类型，
    /// 叠 <c>Content-Security-Policy: sandbox</c>：<c>&lt;img&gt;</c> 照常渲染，当文档打开时脚本不执行。
    /// </remarks>
    protected virtual IActionResult OriginalBytes(FileRecord record, Stream stream)
    {
        var contentType = string.IsNullOrWhiteSpace(record.ContentType)
            ? FileTypeHelper.GetContentType(record.Extension)
            : record.ContentType;

        if (FileTypeHelper.IsInlineRenderable(contentType))
        {
            return File(stream, contentType);
        }

        Response.Headers.ContentSecurityPolicy = "sandbox";
        return File(stream, contentType, record.OriginalName ?? record.FileName);
    }
}

