namespace Tnzi.AI.Infrastructure.Readability;

/// <summary>
/// HTML 可读性提取接口 — 从 HTML 中提取文章正文内容
/// </summary>
public interface IReadabilityExtractor
{
    /// <summary>
    /// 从 HTML 中提取文章内容
    /// </summary>
    /// <param name="html">原始 HTML 字符串</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>提取的文章内容；无法提取时返回 null</returns>
    Task<ArticleContent?> ExtractAsync(string html, CancellationToken ct = default);
}
