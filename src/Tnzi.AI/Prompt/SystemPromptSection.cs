namespace Tnzi.AI.Prompt;

/// <summary>
/// 系统提示词段落 — 一个 XML 标签包裹的内容块
/// </summary>
/// <param name="Tag">XML 标签名（如 "soul", "memory"）</param>
/// <param name="Content">段落内容文本</param>
/// <param name="Order">排序序号（越小越先出现）</param>
public record SystemPromptSection(string Tag, string? Content, int Order);
