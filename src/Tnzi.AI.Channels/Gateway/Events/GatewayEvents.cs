using Tnzi.EventBus;

namespace Tnzi.AI.Channels.Gateway.Events;

/// <summary>
/// Gateway 客户端连接事件
/// </summary>
public class GatewayClientConnectedEvent : EventBase
{
    public string ConnectionId { get; init; } = string.Empty;
    public string? UserId { get; init; }
    public string ClientType { get; init; } = string.Empty;
}

/// <summary>
/// Gateway 客户端断开连接事件
/// </summary>
public class GatewayClientDisconnectedEvent : EventBase
{
    public string ConnectionId { get; init; } = string.Empty;
    public string? Reason { get; init; }
}
