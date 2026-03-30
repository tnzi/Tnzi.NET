namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// MiniMax 消息处理器 — 处理 inline &lt;think&gt; 标签和 reasoning_details 字段。
/// </summary>
/// <remarks>
/// MiniMax 模型在流式和非流式响应中使用 inline &lt;think&gt;...&lt;/think&gt; 标签包裹推理内容，
/// 以及 reasoning_details/reasoning_split 字段。此处理器清理这些标签，
/// 将推理内容提取到 AdditionalProperties 中以保持消息格式一致。
/// </remarks>
public partial class MiniMaxChatMessageProcessor : IChatMessageProcessor
{
    [GeneratedRegex(@"<think>([\s\S]*?)</think>")]
    private static partial Regex ThinkTagRegex();

    public string ProviderName => "minimax";

    public IReadOnlyList<ChatMessage> ProcessOutgoing(IEnumerable<ChatMessage> messages)
    {
        var result = new List<ChatMessage>();
        foreach (var msg in messages)
        {
            result.Add(msg.Role == ChatRole.Assistant ? StripThinkTags(msg) : msg);
        }
        return result;
    }

    public IReadOnlyList<ChatMessage> ProcessIncoming(IEnumerable<ChatMessage> messages)
    {
        var result = new List<ChatMessage>();
        foreach (var msg in messages)
        {
            result.Add(msg.Role == ChatRole.Assistant ? ExtractThinkContent(msg) : msg);
        }
        return result;
    }

    private static ChatMessage ExtractThinkContent(ChatMessage msg)
    {
        var textContent = msg.Text;
        if (string.IsNullOrEmpty(textContent))
            return msg;

        var match = ThinkTagRegex().Match(textContent);
        if (!match.Success)
            return msg;

        var thinkContent = match.Groups[1].Value.Trim();
        var cleanedText = ThinkTagRegex().Replace(textContent, string.Empty).Trim();

        var newMsg = CloneWithText(msg, cleanedText);
        newMsg.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        newMsg.AdditionalProperties["reasoning_content"] = thinkContent;

        return newMsg;
    }

    private static ChatMessage StripThinkTags(ChatMessage msg)
    {
        var textContent = msg.Text;
        if (string.IsNullOrEmpty(textContent) || !textContent.Contains("<think>"))
            return msg;

        var cleanedText = ThinkTagRegex().Replace(textContent, string.Empty).Trim();
        return cleanedText == textContent ? msg : CloneWithText(msg, cleanedText);
    }

    /// <summary>
    /// 创建新消息并复制 AdditionalProperties
    /// </summary>
    private static ChatMessage CloneWithText(ChatMessage source, string newText)
    {
        var newMsg = new ChatMessage(source.Role, newText);
        if (source.AdditionalProperties != null)
        {
            newMsg.AdditionalProperties = new AdditionalPropertiesDictionary();
            foreach (var kvp in source.AdditionalProperties)
            {
                newMsg.AdditionalProperties[kvp.Key] = kvp.Value;
            }
        }
        return newMsg;
    }
}
