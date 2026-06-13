using System.Text.Json.Serialization;

namespace Tnzi.AI.Channels.Gateway.Models;

/// <summary>
/// Gateway 连接信息 — 描述一个 WebSocket 连接
/// </summary>
public class GatewayConnectionInfo
{
    /// <summary>连接 ID</summary>
    [JsonPropertyName("connectionId")]
    public string ConnectionId { get; init; } = string.Empty;

    /// <summary>用户 ID</summary>
    [JsonPropertyName("userId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; init; }

    /// <summary>客户端类型（当前 WebSocket 入口固定为 "websocket"；"device" 类型已随 2026-06-10 服务端重定位移除，字段保留给未来客户端类型）</summary>
    [JsonPropertyName("clientType")]
    public string ClientType { get; init; } = string.Empty;

    /// <summary>设备节点 ID（Device 模块已移除；字段保留供分布式部署标识连接来源节点）</summary>
    [JsonPropertyName("deviceNodeId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeviceNodeId { get; init; }

    /// <summary>连接建立时间</summary>
    [JsonPropertyName("connectedAt")]
    public DateTimeOffset ConnectedAt { get; init; }
}

/// <summary>
/// 在线状态变更事件
/// </summary>
public record PresenceChangedEvent(
    string ConnectionId,
    string? UserId,
    string ClientType,
    bool IsOnline,
    DateTimeOffset Timestamp);
