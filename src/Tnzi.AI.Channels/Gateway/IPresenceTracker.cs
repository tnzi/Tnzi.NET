namespace Tnzi.AI.Channels.Gateway;

/// <summary>
/// 在线状态追踪器 - 管理 Gateway 连接的上下线状态
/// </summary>
[ExperimentalApi(Reason = "Gateway API under active development")]
public interface IPresenceTracker
{
    /// <summary>追踪新连接</summary>
    void TrackConnection(GatewayConnectionInfo connection);

    /// <summary>移除连接</summary>
    void RemoveConnection(string connectionId);

    /// <summary>获取当前连接列表</summary>
    IReadOnlyList<GatewayConnectionInfo> GetConnections();

    /// <summary>在线状态变更事件</summary>
    event Action<PresenceChangedEvent>? OnPresenceChanged;
}
