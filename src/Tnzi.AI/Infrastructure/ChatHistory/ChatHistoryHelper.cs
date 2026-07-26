namespace Tnzi.AI.Infrastructure.ChatHistory;

/// <summary>
/// 聊天历史辅助工具 - 提供 ChatReducer 共享的静态方法
/// </summary>
public static class ChatHistoryHelper
{
    /// <summary>
    /// 将消息按对话轮次分组
    /// </summary>
    /// <remarks>
    /// 一轮从用户消息开始，包含后续所有非用户消息直到下一个用户消息
    /// </remarks>
    public static List<List<ChatMessage>> GroupMessagesByTurns(List<ChatMessage> messages)
    {
        var turns = new List<List<ChatMessage>>();
        List<ChatMessage>? currentTurn = null;

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.User)
            {
                currentTurn = [message];
                turns.Add(currentTurn);
            }
            else if (currentTurn != null)
            {
                currentTurn.Add(message);
            }
            else
            {
                // 没有用户消息的助手/工具消息（不常见，但需要处理）
                turns.Add([message]);
            }
        }

        return turns;
    }

    /// <summary>
    /// 从消息中建立 CallId → ToolName 映射表
    /// </summary>
    /// <remarks>
    /// FunctionResultContent 只有 CallId 没有工具名；需要从同一轮 assistant 消息的 FunctionCallContent 中获取 Name。
    /// </remarks>
    public static Dictionary<string, string> BuildCallIdToToolNameMap(IEnumerable<ChatMessage> messages)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            if (message.Role != ChatRole.Assistant) continue;

            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent functionCall
                    && functionCall.CallId != null
                    && functionCall.Name != null)
                {
                    map[functionCall.CallId] = functionCall.Name;
                }
            }
        }

        return map;
    }

    /// <summary>
    /// 将 ChatMessage 格式化为可读文本（包括工具调用/结果消息）
    /// </summary>
    public static string FormatMessageText(ChatMessage message)
    {
        // 检查是否有非文本内容（FunctionCallContent / FunctionResultContent）
        // 如果只有纯文本，直接返回 Text 属性
        var hasNonTextContent = false;
        foreach (var content in message.Contents)
        {
            if (content is FunctionCallContent or FunctionResultContent)
            {
                hasNonTextContent = true;
                break;
            }
        }

        if (!hasNonTextContent && !string.IsNullOrEmpty(message.Text))
        {
            return message.Text;
        }

        // 遍历所有内容项，确保工具调用/结果也被格式化
        var sb = new StringBuilder();
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case FunctionCallContent call:
                    sb.Append($"[Tool Call: {call.Name ?? "unknown"}");
                    if (call.Arguments is { Count: > 0 })
                    {
                        sb.Append($"({string.Join(", ", call.Arguments.Keys)})");
                    }
                    sb.Append(']');
                    break;

                case FunctionResultContent result:
                    var resultText = result.Result?.ToString();
                    if (!string.IsNullOrEmpty(resultText))
                    {
                        // 截断过长的工具输出
                        const int maxLength = 500;
                        if (resultText.Length > maxLength)
                        {
                            resultText = resultText[..maxLength] + "...(truncated)";
                        }
                        sb.Append($"[Tool Result: {resultText}]");
                    }
                    else
                    {
                        sb.Append("[Tool Result: (empty)]");
                    }
                    break;

                case TextContent text:
                    sb.Append(text.Text);
                    break;
            }
        }

        return sb.Length > 0 ? sb.ToString() : "[non-text content]";
    }
}
