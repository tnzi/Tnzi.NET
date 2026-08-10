namespace Tnzi.Storage.Services;

/// <summary>
/// 文件预览服务实现
/// </summary>
public class FilePreviewService : ApplicationService, IFilePreviewService
{
    private readonly IFileStorage _storage;
    private readonly IDocumentConverter? _documentConverter;

    /// <summary>
    /// 初始化 <see cref="FilePreviewService"/> 类型的新实例。
    /// </summary>
    /// <param name="storage">云存储服务。</param>
    /// <param name="serviceProvider">服务提供者。</param>
    /// <param name="documentConverter">
    /// Office 转 PDF 转换器；来自可选包 <c>Tnzi.Documents</c>，没加载时为 null，
    /// 此时 Office 文档维持「不支持预览」。
    /// </param>
    public FilePreviewService(
        IFileStorage storage,
        IServiceProvider serviceProvider,
        IDocumentConverter? documentConverter = null)
        : base(serviceProvider)
    {
        _storage = Check.NotNull(storage);
        _documentConverter = documentConverter;
    }

    /// <summary>
    /// Office 文档此刻能不能转成 PDF 预览。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 三个条件缺一不可：可选包 <c>Tnzi.Documents</c> 加载了（转换器非 null）、运行环境齐备
    /// （<see cref="IDocumentConverter.IsAvailable"/>，即宿主装了 LibreOffice）、这个格式在支持列表里
    /// （<see cref="IDocumentConverter.CanConvert"/>）。
    /// </para>
    /// <para>
    /// ★ <b><see cref="IDocumentConverter.IsAvailable"/> 不能省。</b>只问 <c>CanConvert</c> 的话，
    /// 「加载了包但没装 LibreOffice」会让 <see cref="CanPreview"/> 答 true，用户点开预览才在
    /// 转换那一步炸成 500 —— 而这恰恰是**默认情形**：<c>Tnzi.Signing</c> 是本包的主要消费者，
    /// 它只用盖章与定位，根本不需要 LibreOffice。
    /// </para>
    /// <para>
    /// 转换器的入参名叫 <c>fileName</c>，这里递的却是扩展名，是因为它的契约声明「只取扩展名」，
    /// 而 <see cref="FileRecord.Extension"/> 全部来自 <c>Path.GetExtension</c>，一定带前导点
    /// （<c>Path.GetExtension(".docx")</c> 就是 <c>".docx"</c>，实测确认）。调用点也都先验过它非空。
    /// </para>
    /// </remarks>
    private bool CanConvertToPdf(string extension)
        => FileTypeHelper.IsOffice(extension)
           && _documentConverter is { IsAvailable: true } converter
           && converter.CanConvert(extension);

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
    public async Task<string> GetPreviewUrlAsync(FileRecord fileRecord)
    {
        if (fileRecord == null || string.IsNullOrEmpty(fileRecord.Path))
            return string.Empty;

        // 对于图片，直接返回URL
        if (FileTypeHelper.IsImage(fileRecord.Extension))
        {
            return await _storage.GetUrlAsync(fileRecord.Path);
        }

        // 对于其他类型，返回预览 API URL。
        // 路由对应 DefaultStoragePreviewController：[Route("files/preview")] + [HttpGet("{id:guid}/preview")]，
        // 框架自动加 "api/" 前缀，故完整路径为 /api/files/preview/{id}/preview。
        var previewType = GetPreviewType(fileRecord);
        return $"/api/files/preview/{fileRecord.Id}/preview?type={previewType}";
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

        // 对于图片，直接返回原文件流
        if (FileTypeHelper.IsImage(extension))
        {
            return await _storage.DownloadAsync(fileRecord.Path!);
        }

        // 对于PDF，直接返回原文件流（浏览器可以预览）
        if (FileTypeHelper.IsPdf(extension))
        {
            return await _storage.DownloadAsync(fileRecord.Path!);
        }

        // 对于文本文件，直接返回原文件流
        if (FileTypeHelper.IsText(extension))
        {
            return await _storage.DownloadAsync(fileRecord.Path!);
        }

        // 对于视频和音频，返回原文件流（浏览器可播放）
        if (FileTypeHelper.IsVideo(extension) || FileTypeHelper.IsAudio(extension))
        {
            return await _storage.DownloadAsync(fileRecord.Path!);
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
        await using var source = await _storage.DownloadAsync(fileRecord.Path!);
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);

        var pdf = await _documentConverter!.ConvertToPdfAsync(buffer.ToArray(), extension);

        // 返回的流交给调用方（控制器 File(...)）dispose，与本方法其它分支一致。
        return new MemoryStream(pdf);
    }
}

