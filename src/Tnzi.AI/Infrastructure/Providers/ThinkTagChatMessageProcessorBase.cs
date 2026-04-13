namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// Base class for providers that use inline &lt;think&gt;...&lt;/think&gt; tags for reasoning content.
/// Extracts think content into AdditionalProperties["reasoning_content"] on incoming messages,
/// and strips think tags from outgoing messages.
/// </summary>
/// <remarks>
/// Used by MiniMax, Kimi (Moonshot), and GLM (Zhipu) providers which all share the same
/// &lt;think&gt; tag format for reasoning/thinking content.
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
