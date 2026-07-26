namespace Tnzi.AI.Infrastructure.Helpers;

/// <summary>
/// AI 文本工具 - 中文检测、代码围栏解析等
/// </summary>
public static partial class AiTextHelper
{
    /// <summary>检测文本是否包含中文字符</summary>
    public static bool ContainsChinese(string text)
        => ChineseCharRegex().IsMatch(text);

    /// <summary>去除 Markdown 代码围栏包裹</summary>
    public static string StripCodeFence(string text)
        => CodeFenceStripRegex().Replace(text, "$1").Trim();

    [GeneratedRegex(@"[\u4e00-\u9fff]")]
    private static partial Regex ChineseCharRegex();

    [GeneratedRegex(@"```(?:json)?\s*([\s\S]*?)\s*```")]
    private static partial Regex CodeFenceStripRegex();
}
