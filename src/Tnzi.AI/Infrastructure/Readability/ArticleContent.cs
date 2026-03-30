namespace Tnzi.AI.Infrastructure.Readability;

/// <summary>
/// 文章提取结果
/// </summary>
/// <param name="Title">文章标题（可能为 null）</param>
/// <param name="TextContent">纯文本内容（去除 HTML 标签）</param>
/// <param name="Author">作者（可能为 null）</param>
/// <param name="PublishDate">发布日期（可能为 null）</param>
/// <param name="Excerpt">摘要（可能为 null）</param>
public record ArticleContent(
    string? Title,
    string TextContent,
    string? Author = null,
    DateTime? PublishDate = null,
    string? Excerpt = null);
