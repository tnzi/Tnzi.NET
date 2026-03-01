
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
        var serialized = new SerializedChatMessage { Role = message.Role.Value };

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case FunctionCallContent fc:
                    serialized.Contents.Add(new SerializedContent
                    {
                        Type = "function_call",
                        CallId = fc.CallId,
                        Name = fc.Name,
                        Arguments = fc.Arguments != null
                            ? JsonSerializer.SerializeToElement(fc.Arguments, SerializerOptions)
                            : null
                    });
                    break;

                case FunctionResultContent fr:
                    serialized.Contents.Add(new SerializedContent
                    {
                        Type = "function_result",
                        CallId = fr.CallId,
                        Result = fr.Result != null
                            ? JsonSerializer.SerializeToElement(fr.Result, SerializerOptions)
                            : null
                    });
                    break;

                case DataContent dc:
                    serialized.Contents.Add(new SerializedContent
                    {
                        // 区分图片和通用二进制数据，保持序列化语义清晰
                        Type = dc.HasTopLevelMediaType("image") ? "image" : "data",
                        Url = dc.Uri?.ToString(),
                        Base64Data = dc.Data is { Length: > 0 } ? Convert.ToBase64String(dc.Data.Span) : null,
                        MediaType = dc.MediaType
                    });
                    break;

                case TextContent tc:
                    serialized.Contents.Add(new SerializedContent
                    {
                        Type = "text",
                        Text = tc.Text
                    });
                    break;

                default:
                    // UsageContent 等运行时元数据不持久化
                    if (content is not UsageContent)
                    {
                        // 回退：保存为文本
                        var text = content.ToString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            serialized.Contents.Add(new SerializedContent
                            {
                                Type = "text",
                                Text = text
                            });
                        }
                    }
                    break;
            }
        }

        return serialized;
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

        // 向后兼容：旧格式只有 Content 字段，没有 Contents 列表
        if (msg.Contents.Count == 0)
        {
            return new ChatMessage(role, msg.Content);
        }

        // 新格式：从 Contents 列表还原 AIContent
        var contents = new List<AIContent>();
        foreach (var item in msg.Contents)
        {
            switch (item.Type)
            {
                case "function_call":
                    var args = item.Arguments is { } argsElement
                        ? JsonSerializer.Deserialize<Dictionary<string, object?>>(argsElement.GetRawText(), SerializerOptions)
                        : null;
                    contents.Add(new FunctionCallContent(item.CallId ?? string.Empty, item.Name ?? string.Empty, args));
                    break;

                case "function_result":
                    object? result = item.Result is { } resultElement
                        ? (object)resultElement
                        : null;
                    contents.Add(new FunctionResultContent(item.CallId ?? string.Empty, result));
                    break;

                case "image":
                case "data":
                    contents.Add(DeserializeDataContent(item));
                    break;

                case "text":
                default:
                    if (item.Text != null)
                    {
                        contents.Add(new TextContent(item.Text));
                    }
                    break;
            }
        }

        return new ChatMessage(role, contents);
    }

    /// <summary>
    /// 反序列化 DataContent（同时处理 image 和 data 类型，MEAI 中图片即 DataContent + image/* mediaType）
    /// </summary>
    private static DataContent DeserializeDataContent(SerializedContent item)
    {
        if (!string.IsNullOrEmpty(item.Base64Data))
        {
            var bytes = Convert.FromBase64String(item.Base64Data);
            return new DataContent(bytes, item.MediaType ?? "application/octet-stream");
        }

        if (!string.IsNullOrEmpty(item.Url))
        {
            return new DataContent(new Uri(item.Url), item.MediaType);
        }

        return new DataContent(ReadOnlyMemory<byte>.Empty, item.MediaType ?? "application/octet-stream");
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
    /// 序列化用的消息结构（向后兼容：旧数据只有 Content 字段）
    /// </summary>
    private class SerializedChatMessage
    {
        public string Role { get; set; } = "user";
        /// <summary>
        /// 旧格式：纯文本内容（向后兼容）
        /// </summary>
        public string? Content { get; set; }
        /// <summary>
        /// 新格式：结构化内容列表（保留 FunctionCall/FunctionResult）
        /// </summary>
        public List<SerializedContent> Contents { get; set; } = [];
    }

    /// <summary>
    /// 序列化用的内容项结构
    /// </summary>
    private class SerializedContent
    {
        /// <summary>
        /// 内容类型鉴别器：text, function_call, function_result, image, data
        /// </summary>
        public string Type { get; set; } = "text";
        /// <summary>
        /// 文本内容（type=text）
        /// </summary>
        public string? Text { get; set; }
        /// <summary>
        /// 工具调用/结果 ID（type=function_call, function_result）
        /// </summary>
        public string? CallId { get; set; }
        /// <summary>
        /// 工具名称（type=function_call）
        /// </summary>
        public string? Name { get; set; }
        /// <summary>
        /// 工具调用参数（type=function_call）
        /// </summary>
        public JsonElement? Arguments { get; set; }
        /// <summary>
        /// 工具执行结果（type=function_result）
        /// </summary>
        public JsonElement? Result { get; set; }
        /// <summary>
        /// 资源 URL（type=image, data）
        /// </summary>
        public string? Url { get; set; }
        /// <summary>
        /// Base64 编码数据（type=image, data）
        /// </summary>
        public string? Base64Data { get; set; }
        /// <summary>
        /// 媒体类型（type=image, data）
        /// </summary>
        public string? MediaType { get; set; }
    }
}
