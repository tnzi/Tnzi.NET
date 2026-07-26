namespace Tnzi.AI.Engine;

/// <summary>
/// ChatMessage 工具方法 - 提供消息文本提取和流式 Token 使用信息解析
/// </summary>
public static class ChatMessageHelper
{
    /// <summary>
    /// 从 ChatMessage 中提取用于 guardrail 检查和 token 预估的文本。
    /// 纯文本消息直接返回 Text；多模态消息拼接所有 TextContent 并为图片追加固定 token 估算占位。
    /// </summary>
    public static string ExtractEstimationText(ChatMessage message)
    {
        // 纯文本模式（大多数请求）
        if (message.Text != null && (message.Contents == null || message.Contents.Count <= 1))
        {
            return message.Text;
        }

        // 多模态模式：拼接文本 + 图片占位
        if (message.Contents == null || message.Contents.Count == 0)
        {
            return message.Text ?? string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var content in message.Contents)
        {
            if (content is TextContent textContent)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(textContent.Text);
            }
            else if (content is DataContent)
            {
                // 图片 token 估算：中等分辨率图片约 765 tokens（OpenAI），低估比不估好
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(new string('X', 3000)); // ~750 tokens at 4 chars/token
            }
        }

        return sb.Length > 0 ? sb.ToString() : (message.Text ?? string.Empty);
    }

    /// <summary>
    /// 从流式 chunk 中提取 Token 使用信息
    /// </summary>
    public static (int inputTokens, int outputTokens) ExtractStreamingUsage(AgentStreamChunk chunk)
    {
        if (chunk.Usage != null)
        {
            return (chunk.Usage.InputTokens, chunk.Usage.OutputTokens);
        }
        return (0, 0);
    }
}
