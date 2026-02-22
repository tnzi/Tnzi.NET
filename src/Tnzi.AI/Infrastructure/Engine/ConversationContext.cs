
namespace Tnzi.AI.Infrastructure.Engine;

/// <summary>
/// 对话上下文 — 纯 JSON 序列化，无反射
/// </summary>
public class ConversationContext
{
    /// <summary>
    /// 消息列表
    /// </summary>
    public List<ChatMessage> Messages { get; set; } = [];

    /// <summary>
    /// 元数据（可扩展字段，如 reducer 状态等）
    /// </summary>
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    /// <summary>
    /// 序列化为 JSON 字符串
    /// </summary>
    public string Serialize()
    {
        var data = new ConversationContextData
        {
            Messages = Messages.Select(SerializeMessage).ToList(),
            Metadata = Metadata
        };
        return JsonSerializer.Serialize(data, SerializerOptions);
    }

    /// <summary>
    /// 从 JSON 字符串反序列化
    /// </summary>
    /// <returns>反序列化后的 ConversationContext，失败时返回 null</returns>
    public static ConversationContext? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var data = JsonSerializer.Deserialize<ConversationContextData>(json, SerializerOptions);
            if (data == null) return null;

            var context = new ConversationContext
            {
                Metadata = data.Metadata
            };

            if (data.Messages != null)
            {
                foreach (var msg in data.Messages)
                {
                    context.Messages.Add(DeserializeMessage(msg));
                }
            }

            return context;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SerializedChatMessage SerializeMessage(ChatMessage message)
    {
        return new SerializedChatMessage
        {
            Role = message.Role.Value,
            Content = message.Text
        };
    }

    private static ChatMessage DeserializeMessage(SerializedChatMessage msg)
    {
        var role = msg.Role switch
        {
            "system" => ChatRole.System,
            "assistant" => ChatRole.Assistant,
            "tool" => ChatRole.Tool,
            _ => ChatRole.User
        };
        return new ChatMessage(role, msg.Content);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 序列化用的内部数据结构
    /// </summary>
    private class ConversationContextData
    {
        public List<SerializedChatMessage>? Messages { get; set; }
        public Dictionary<string, JsonElement>? Metadata { get; set; }
    }

    /// <summary>
    /// 序列化用的消息结构
    /// </summary>
    private class SerializedChatMessage
    {
        public string Role { get; set; } = "user";
        public string? Content { get; set; }
    }
}
