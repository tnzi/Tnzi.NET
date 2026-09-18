namespace Tnzi.Storage.Services;

/// <summary>
/// 文件预览服务实现
/// </summary>
public class FilePreviewService : ApplicationService, IFilePreviewService
{
    private readonly IFileStorageService _fileStorageService;
    private readonly IDocumentConverter? _documentConverter;

    /// <summary>
    /// 初始化 <see cref="FilePreviewService"/> 类型的新实例。
    /// </summary>
    /// <param name="fileStorageService">
    /// 文件存储服务。预览的字节经它的 <see cref="IFileStorageService.GetForPreviewAsync"/> 取 ——
    /// 而不是直接找 provider —— 这样 <c>FileAccessedEvent</c> 的发布点只有一处，预览才会以
    /// <c>FileAccessType.Preview</c> 出现在审计里（此前这条路径一条事件都不发）。
    /// </param>
    /// <param name="serviceProvider">服务提供者。</param>
    /// <param name="documentConverter">
    /// Office 转 PDF 转换器；来自可选包 <c>Tnzi.Documents</c>，没加载时为 null，
    /// 此时 Office 文档维持「不支持预览」。
    /// </param>
    public FilePreviewService(
        IFileStorageService fileStorageService,
        IServiceProvider serviceProvider,
        IDocumentConverter? documentConverter = null)
        : base(serviceProvider)
    {
        _fileStorageService = Check.NotNull(fileStorageService);
        _documentConverter = documentConverter;
    }

    /// <summary>
    /// Office 文档此刻能不能转成 PDF 预览。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 两个条件缺一不可：可选包 <c>Tnzi.Documents</c> 加载了（转换器非 null）、
    /// <b>这个扩展名</b>此刻转得动（<see cref="IDocumentConverter.IsAvailableFor"/>，
    /// 即格式在支持列表里**且**它背后那个引擎的运行环境齐备）。
    /// </para>
    /// <para>
    /// ★ <b>不能只问 <see cref="IDocumentConverter.CanConvert"/>。</b>那样「加载了包但没装 LibreOffice」
    /// 会让 <see cref="CanPreview"/> 答 true，用户点开预览才在转换那一步炸成 500 —— 而这恰恰是
    /// **默认情形**：<c>Tnzi.Signing</c> 是本包的主要消费者，它只用盖章与定位，根本不需要 LibreOffice。
    /// </para>
    /// <para>
    /// ★ <b>也不能只问 <see cref="IDocumentConverter.IsAvailable"/>。</b>框架的默认转换器背后有两个引擎
    /// （HTML 交浏览器、其余交 LibreOffice），该属性只回答「有没有任何引擎能干活」；
    /// 在「装了浏览器、没装 LibreOffice」的宿主上，拿它去判断 <c>.docx</c> 会得到肯定答复。
    /// <c>IsAvailableFor</c> 问的才是这里真正要问的那个问题。
    /// </para>
    /// <para>
    /// 转换器的入参名叫 <c>fileName</c>，这里递的却是扩展名，是因为它的契约声明「只取扩展名」，
    /// 而 <see cref="FileRecord.Extension"/> 全部来自 <c>Path.GetExtension</c>，一定带前导点
    /// （<c>Path.GetExtension(".docx")</c> 就是 <c>".docx"</c>，实测确认）。调用点也都先验过它非空。
    /// </para>
    /// </remarks>
    private bool CanConvertToPdf(string extension)
        => FileTypeHelper.IsOffice(extension)
           && _documentConverter?.IsAvailableFor(extension) == true;

    /// <summary>
    /// 检查文件是否支持预览。
    /// </summary>
    /// <param name="fileRecord">文件记录</param>
    /// <returns>是否支持预览</returns>
    public bool CanPreview(FileRecord fileRecord)
    {
        if (fileRecord == null || string.IsNullOrEmpty(fileRecord.Extension))
            return false;

        var extension = fileRecord.Extension;

        // Office 文档只在转换器可用时才算可预览 —— 这条判定必须与 GeneratePreviewAsync 一致，
        // 因为控制器拿它当闸门：返回 false 时那边直接 400，压根不会调到生成方法。
        return FileTypeHelper.IsImage(extension) ||
               FileTypeHelper.IsPdf(extension) ||
               FileTypeHelper.IsVideo(extension) ||
               FileTypeHelper.IsAudio(extension) ||
               FileTypeHelper.IsText(extension) ||
               CanConvertToPdf(extension);
    }

    /// <summary>
    /// 获取文件预览URL
    /// </summary>
    /// <param name="fileRecord">文件记录</param>
    /// <returns>预览URL</returns>
    /// <remarks>
    /// 一律回 API 路由，图片也不例外。此前图片走 <c>IFileStorage.GetUrlAsync(path)</c>：不带过期的
    /// S3 / Azure 返回的是 <c>base + key</c> —— 一条永久、无签名、一经发出就再不过问
    /// <see cref="IFileAccessAuthorizer"/> 的链接（公开桶上就是一份永久副本），本地 provider 没配
    /// <c>UrlPrefix</c> 时甚至回的是相对键而不是 URL；两种形态都把 <c>FileRecordDto</c> 刻意不外露的
    /// 存储键交了出去。需要直连对象存储的调用方走 <c>presigned-url</c>（有 TTL 上限、有 <c>[SensitiveEndpoint]</c>）。
    /// <para>
    /// 两条 API 路由按类型分：浏览器能直接显示的类型（<see cref="FileTypeHelper.IsInlineRenderable"/>）回
    /// <c>/api/files/{id}/preview</c> —— 匿名可达（公开文件 / <c>?sig=</c>）、每次都过授权器、带缓存头，
    /// <c>&lt;img src&gt;</c> 在 Bearer 交付模式下拼上 <c>sig</c> 就能加载；其余类型（Office 转 PDF、SVG 等）回
    /// <c>/api/files/preview/{id}/preview?type=…</c>，那个控制器是类级 <c>[ApiAuthorize]</c>，登录才可达，
    /// 也只有它会做转换。
    /// </para>
    /// </remarks>
    public Task<string> GetPreviewUrlAsync(FileRecord fileRecord)
    {
        if (fileRecord == null || string.IsNullOrEmpty(fileRecord.Path))
            return Task.FromResult(string.Empty);

        // 路由对应 DefaultStorageController：[Route("files")] + [HttpGet("{id:guid}/preview")]，
        // 与它内联的判据同一个（不在白名单里的类型那条路由会按附件发出，装不进 <img>）。
        if (FileTypeHelper.IsInlineRenderable(fileRecord.ContentType))
            return Task.FromResult($"/api/files/{fileRecord.Id}/preview");

        // 路由对应 DefaultStoragePreviewController：[Route("files/preview")] + [HttpGet("{id:guid}/preview")]，
        // 框架自动加 "api/" 前缀，故完整路径为 /api/files/preview/{id}/preview。
        var previewType = GetPreviewType(fileRecord);
        return Task.FromResult($"/api/files/preview/{fileRecord.Id}/preview?type={previewType}");
    }

    /// <summary>
    /// 获取文件预览类型
    /// </summary>
    /// <param name="fileRecord">文件记录</param>
    /// <returns>预览类型</returns>
    public string GetPreviewType(FileRecord fileRecord)
    {
        if (fileRecord == null || string.IsNullOrEmpty(fileRecord.Extension))
            return "unknown";

        var extension = fileRecord.Extension;

        if (FileTypeHelper.IsImage(extension))
            return "image";
        if (FileTypeHelper.IsPdf(extension))
            return "pdf";
        if (FileTypeHelper.IsVideo(extension))
            return "video";
        if (FileTypeHelper.IsAudio(extension))
            return "audio";
        if (FileTypeHelper.IsText(extension))
            return "text";

        // Office 文档转换后产出的**就是** PDF，如实报告：控制器据此定 Content-Type，
        // 前端据此选查看器。转换器没加载时仍报 "office"（配合 CanPreview=false，即不可预览）。
        if (CanConvertToPdf(extension))
            return "pdf";
        if (FileTypeHelper.IsOffice(extension))
            return "office";

        return "unknown";
    }

    /// <summary>
    /// 生成文件预览内容
    /// </summary>
    /// <param name="fileRecord">文件记录</param>
    /// <returns>预览内容。</returns>
    public async Task<Stream> GeneratePreviewAsync(FileRecord fileRecord)
    {
        Check.NotNull(fileRecord);
        Check.NotNullOrEmpty(fileRecord.Path!);

        var extension = fileRecord.Extension;

        // 图片 / PDF / 文本 / 视频 / 音频：浏览器能直接显示，原样返回文件流
        if (FileTypeHelper.IsImage(extension)
            || FileTypeHelper.IsPdf(extension)
            || FileTypeHelper.IsText(extension)
            || FileTypeHelper.IsVideo(extension)
            || FileTypeHelper.IsAudio(extension))
        {
            return await OpenForPreviewAsync(fileRecord);
        }

        // 对于 Office 文档，转成 PDF 后返回（浏览器可预览）
        if (CanConvertToPdf(extension))
        {
            return await ConvertOfficeToPdfAsync(fileRecord, extension);
        }

        if (FileTypeHelper.IsOffice(extension))
        {
            throw new NotSupportedException(
                "Office document preview requires the optional Tnzi.Documents module and LibreOffice on the host. " +
                "Please download the file and open it with your local application.");
        }

        // 其他类型不支持预览
        throw new NotSupportedException($"Preview is not supported for file type: {extension}");
    }

    /// <summary>
    /// 下载原件并转成 PDF。
    /// </summary>
    /// <remarks>
    /// 转换器契约收 <c>byte[]</c>（它要把内容落成临时文件交给外部进程），所以这里必须整份读进内存。
    /// 上限由 <c>Storage:MaxFileSize</c> 在上传那一侧就已经约束住。
    /// </remarks>
    private async Task<Stream> ConvertOfficeToPdfAsync(FileRecord fileRecord, string extension)
    {
        await using var source = await OpenForPreviewAsync(fileRecord);
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);

        var pdf = await _documentConverter!.ConvertToPdfAsync(buffer.ToArray(), extension);

        // 返回的流交给调用方（控制器 File(...)）dispose，与本方法其它分支一致。
        return new MemoryStream(pdf);
    }

    /// <summary>
    /// 经存储服务取预览字节。调用方（控制器）已经用 <c>GetRecordAsync</c> 授权过一次，这里再过一次是
    /// 取流路径自带的；失败只剩「记录刚被删 / 权限刚被收回」这类竞态，按 404 抛出。
    /// </summary>
    private async Task<Stream> OpenForPreviewAsync(FileRecord fileRecord)
    {
        var result = await _fileStorageService.GetForPreviewAsync(fileRecord.Id);
        if (!result.Succeeded)
        {
            throw new ResourceNotFoundException("File", fileRecord.Id);
        }

        return result.Data!;
    }
}

