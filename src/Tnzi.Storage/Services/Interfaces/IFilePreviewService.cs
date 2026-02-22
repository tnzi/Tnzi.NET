namespace Tnzi.Storage.Services;

/// <summary>
/// 文件预览服务接口
/// </summary>
public interface IFilePreviewService
{
    /// <summary>
    /// 检查文件是否支持预览。
    /// </summary>
    /// <param name="fileRecord">文件记录</param>
    /// <returns>是否支持预览</returns>
    bool CanPreview(FileRecord fileRecord);

    /// <summary>
    /// 获取文件预览URL
    /// </summary>
    /// <param name="fileRecord">文件记录</param>
    /// <returns>预览URL</returns>
    Task<string> GetPreviewUrlAsync(FileRecord fileRecord);

    /// <summary>
    /// 获取文件预览类型（image, pdf, video, audio, text等）
    /// </summary>
    /// <param name="fileRecord">文件记录</param>
    /// <returns>预览类型</returns>
    string GetPreviewType(FileRecord fileRecord);

    /// <summary>
    /// 生成文件预览内容（用于在线预览）
    /// </summary>
    /// <param name="fileRecord">文件记录</param>
    /// <returns>预览内容。</returns>
    Task<Stream> GeneratePreviewAsync(FileRecord fileRecord);
}

