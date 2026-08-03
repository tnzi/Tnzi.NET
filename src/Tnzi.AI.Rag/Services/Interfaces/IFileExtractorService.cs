namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 文件文本提取服务接口
/// </summary>
public interface IFileExtractorService
{
    /// <summary>
    /// 判断是否支持指定文件类型
    /// </summary>
    bool Supports(string fileName);

    /// <summary>
    /// 从文件流中提取纯文本
    /// </summary>
    Task<string> ExtractTextAsync(Stream content, string fileName, CancellationToken ct = default);
}
