namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// Base class for providers that use inline &lt;think&gt;...&lt;/think&gt; tags for reasoning content.
/// Extracts think content into AdditionalProperties["reasoning_content"] on incoming messages,
/// and strips think tags from outgoing messages.
/// </summary>
/// <remarks>
/// <para>
/// Used by MiniMax, Kimi (Moonshot), and GLM (Zhipu) providers which all share the same
/// &lt;think&gt; tag format for reasoning/thinking content.
/// </para>
/// <para>
/// 只改写正文：这些提供商在同一条助手消息里同时返回内联 &lt;think&gt; 正文与 tool_calls，
/// 重建消息时其余内容（工具调用、工具结果、用量、数据）与消息身份必须原样保留。
/// 只留正文 = 非流式执行器在 response.Messages 里找不到工具调用，运行安静地结束；
/// 出站历史里丢掉工具调用 = 紧随其后的工具结果成了孤儿，提供商拒绝整个请求。
/// </para>
/// </remarks>
public abstract partial class ThinkTagChatMessageProcessorBase : IChatMessageProcessor
{
    [GeneratedRegex(@"<think>([\s\S]*?)</think>")]
    private static partial Regex ThinkTagRegex();

    public abstract string ProviderName { get; }

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

        var matches = ThinkTagRegex().Matches(textContent);
        if (matches.Count == 0)
            return msg;

        var thinkContent = string.Join('\n', matches.Select(m => m.Groups[1].Value.Trim()).Where(t => t.Length > 0));
        var cleanedText = ThinkTagRegex().Replace(textContent, string.Empty).Trim();

        var newMsg = CloneWithText(msg, cleanedText);
        newMsg.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        newMsg.AdditionalProperties["reasoning_content"] = thinkContent;
        // 非流式路径的推理由 AgentExecutor.ExtractReasoning 从 TextReasoningContent 读，不读 AdditionalProperties
        if (thinkContent.Length > 0)
        {
            newMsg.Contents.Insert(0, new TextReasoningContent(thinkContent));
        }

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
    /// 复制消息，把全部正文（<see cref="TextContent"/>）合并成一段 <paramref name="newText"/> 放在第一段正文的位置，
    /// 其余内容按原顺序保留；新正文为空时不留空的 <see cref="TextContent"/>。
    /// </summary>
    /// <remarks>
    /// 正文按合并后的整段处理（<see cref="ChatMessage.Text"/> 的口径），标签被切在两段 TextContent 之间也能剥掉。
    /// </remarks>
    private static ChatMessage CloneWithText(ChatMessage source, string newText)
    {
        var contents = new List<AIContent>(source.Contents.Count);
        var textPlaced = false;
        foreach (var content in source.Contents)
        {
            if (content is TextContent)
            {
                if (!textPlaced && newText.Length > 0)
                {
                    contents.Add(new TextContent(newText));
                }
                textPlaced = true;
                continue;
            }

            contents.Add(content);
        }

        return new ChatMessage(source.Role, contents)
        {
            AuthorName = source.AuthorName,
            MessageId = source.MessageId,
            CreatedAt = source.CreatedAt,
            RawRepresentation = source.RawRepresentation,
            AdditionalProperties = source.AdditionalProperties is null
                ? null
                : new AdditionalPropertiesDictionary(source.AdditionalProperties)
        };
    }
}
