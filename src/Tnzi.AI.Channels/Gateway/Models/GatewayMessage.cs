using System.Text.Json.Serialization;

namespace Tnzi.AI.Channels.Gateway.Models;

/// <summary>
/// WebSocket 协议消息 — Gateway 与客户端之间的通信载体
/// </summary>
public class GatewayMessage
{
    /// <summary>消息类型: request / response / event</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>消息 ID（用于请求-响应配对，event 类型可为 null）</summary>
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    /// <summary>方法名（如 chat.send, session.bind）</summary>
    [JsonPropertyName("method")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Method { get; init; }

    /// <summary>负载数据</summary>
    [JsonPropertyName("payload")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Payload { get; init; }

    /// <summary>错误信息（仅 response 类型使用）</summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}
