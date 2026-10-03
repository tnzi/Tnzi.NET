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

    /// <summary>标题输出预算的下界（模型偶尔会先吐一点前缀）。</summary>
    private const int MinTitleMaxTokens = 32;

    /// <summary>标题输出预算的上界（再长也不是标题了）。</summary>
    private const int MaxTitleMaxTokens = 256;

    /// <summary>
    /// 根据对话内容生成简短标题
    /// </summary>
    /// <remarks>
    /// 标题是 utility 调用里最窄的那一个，所以它**自己**传一个小的输出预算，
    /// 而不是让共享默认值 <c>AI:Utility:MaxTokens</c> 按它的需要缩到问答用不了的量级。
    /// 调用方显式传了 <see cref="AiUtilityCallOptions.MaxTokens"/> 时以调用方为准。
    /// </remarks>
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

        if (options?.MaxTokens is null)
        {
            options = new AiUtilityCallOptions
            {
                Model = options?.Model,
                Temperature = options?.Temperature,
                Provider = options?.Provider,
                // 每个字符按最坏情况算一个 token，再留一点结构开销
                MaxTokens = Math.Clamp(maxLength * 2, MinTitleMaxTokens, MaxTitleMaxTokens)
            };
        }

        var result = await utility.ExecuteAsync(systemPrompt, content, options, cancellationToken);

        if (string.IsNullOrWhiteSpace(result))
            return null;

        // 清理 AI 可能添加的引号
        result = result.Trim('"', '\'', '\u201C', '\u201D');

        // 截断到 maxLength（尊重多字节字符）
        return result.TruncateByTextElements(maxLength);
    }
}
