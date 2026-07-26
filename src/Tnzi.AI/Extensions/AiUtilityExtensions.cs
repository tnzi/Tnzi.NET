namespace Tnzi.AI.Extensions;

/// <summary>
/// IAiUtility 扩展方法 - 提供常用 AI 任务的便捷封装
/// </summary>
public static class AiUtilityExtensions
{
    private const string TitleSystemPrompt =
        "Generate a concise, descriptive title for the following conversation. " +
        "The title should capture the main topic or intent. " +
        "Reply with ONLY the title text, no quotes, no punctuation at the end, no explanation. " +
        "Use the same language as the conversation. " +
        "Maximum {0} characters.";

    /// <summary>
    /// 根据对话内容生成简短标题
    /// </summary>
    public static async Task<string?> GenerateTitleAsync(
        this IAiUtility utility,
        string content,
        int maxLength = 50,
        AiUtilityCallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(utility);
        Check.NotNullOrWhiteSpace(content);

        var systemPrompt = string.Format(CultureInfo.InvariantCulture, TitleSystemPrompt, maxLength);

        var result = await utility.ExecuteAsync(systemPrompt, content, options, cancellationToken);

        if (string.IsNullOrWhiteSpace(result))
            return null;

        // 清理 AI 可能添加的引号
        result = result.Trim('"', '\'', '\u201C', '\u201D');

        // 截断到 maxLength（尊重多字节字符）
        return result.TruncateByTextElements(maxLength);
    }
}
