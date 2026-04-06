using System.Text.Json.Serialization;

namespace Tnzi.AI.Channels.Gateway.Models;

/// <summary>
/// Gateway 入站请求 — 客户端发送给 Gateway 的聊天请求
/// </summary>
public class GatewayRequest
{
    /// <summary>频道名称（如 telegram, slack, web）</summary>
    [JsonPropertyName("channel")]
    public string Channel { get; init; } = string.Empty;

    /// <summary>聊天/群组 ID</summary>
    [JsonPropertyName("chatId")]
    public string ChatId { get; init; } = string.Empty;

    /// <summary>用户 ID</summary>
    [JsonPropertyName("userId")]
    public string UserId { get; init; } = string.Empty;

    /// <summary>话题 ID（可选，用于 Thread 模式）</summary>
    [JsonPropertyName("topicId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TopicId { get; init; }

    /// <summary>Peer 类型（如 user, bot, group）</summary>
    [JsonPropertyName("peerKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PeerKind { get; init; }

    /// <summary>用户消息文本</summary>
    [JsonPropertyName("userMessage")]
    public string UserMessage { get; init; } = string.Empty;

    /// <summary>目标 Agent ID（可选，不指定时使用默认绑定）</summary>
    [JsonPropertyName("agentId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? AgentId { get; init; }

    /// <summary>线程 ID（可选，续接已有对话）</summary>
    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ThreadId { get; init; }

    /// <summary>附加元数据</summary>
    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? Metadata { get; init; }
}
