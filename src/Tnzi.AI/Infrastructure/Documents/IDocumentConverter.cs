namespace Tnzi.AI.Infrastructure.Documents;

/// <summary>
/// 文档转换接口 — 将各种文档格式转换为 Markdown 文本
/// </summary>
public interface IDocumentConverter
{
    /// <summary>
    /// 判断是否支持指定扩展名的文档转换
    /// </summary>
    /// <param name="extension">文件扩展名（含点号，如 ".pdf"）</param>
    bool CanConvert(string extension);

    /// <summary>
    /// 将文档转换为 Markdown 文本
    /// </summary>
    /// <param name="filePath">文档文件路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>Markdown 文本，转换失败返回 null</returns>
    Task<string?> ConvertToMarkdownAsync(string filePath, CancellationToken ct = default);
}
