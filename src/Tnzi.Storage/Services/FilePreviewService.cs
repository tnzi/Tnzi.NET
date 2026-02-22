namespace Tnzi.Storage.Services;

/// <summary>
/// 文件预览服务实现
/// </summary>
public class FilePreviewService : ApplicationService, IFilePreviewService
{
    private readonly IFileStorage _storage;

    /// <summary>
    /// 初始化 <see cref="FilePreviewService"/> 类型的新实例。
    /// </summary>
    /// <param name="storage">云存储服务。</param>
    /// <param name="serviceProvider">服务提供者。</param>
    public FilePreviewService(
        IFileStorage storage,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _storage = Check.NotNull(storage);
    }

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

        // 支持的预览类型
        return FileTypeHelper.IsImage(extension) ||
               FileTypeHelper.IsPdf(extension) ||
               FileTypeHelper.IsVideo(extension) ||
               FileTypeHelper.IsAudio(extension) ||
               FileTypeHelper.IsText(extension) ||
               FileTypeHelper.IsOffice(extension);
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

        // 对于其他类型，返回预览API URL
        var previewType = GetPreviewType(fileRecord);
        return $"/api/file/preview/{fileRecord.Id}?type={previewType}";
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

        // 对于 Office 文档，需要转换为 PDF 或 HTML
        // 注意：Office 文档转换功能计划在后续版本中实现。
        // 实现方案选项：
        // 1. 使用 LibreOffice 命令行工具进行转换（需安装 LibreOffice）
        // 2. 使用在线转换服务（如 CloudConvert、Zamzar 等）
        // 3. 使用 Microsoft Graph API（仅限 Office 365 环境）
        // 当前版本：不支持预览，抛出明确异常
        if (FileTypeHelper.IsOffice(extension))
        {
            throw new NotSupportedException(
                "Office document preview is not yet implemented. " +
                "Please download the file and open it with your local application. " +
                "This feature is planned for a future release.");
        }

        // 其他类型不支持预览
        throw new NotSupportedException($"Preview is not supported for file type: {extension}");
    }

}

