using Tnzi.AI.Channels.Gateway.Events;
using Tnzi.EventBus;

namespace Tnzi.AI.Channels.Gateway;

/// <summary>
/// 默认在线状态追踪器 — ConcurrentDictionary 实现，线程安全
/// </summary>
public class DefaultPresenceTracker : IPresenceTracker
{
    private readonly ConcurrentDictionary<string, GatewayConnectionInfo> _connections = new();
    private readonly IEventBus? _eventBus;
    private readonly ILogger<DefaultPresenceTracker> _logger;

    public DefaultPresenceTracker(ILogger<DefaultPresenceTracker> logger, IEventBus? eventBus = null)
    {
        _logger = Check.NotNull(logger);
        _eventBus = eventBus;
    }

    /// <inheritdoc />
    public event Action<PresenceChangedEvent>? OnPresenceChanged;

    /// <inheritdoc />
    public void TrackConnection(GatewayConnectionInfo connection)
    {
        Check.NotNull(connection);
        Check.NotNullOrWhiteSpace(connection.ConnectionId);

        _connections[connection.ConnectionId] = connection;

        OnPresenceChanged?.Invoke(new PresenceChangedEvent(
            connection.ConnectionId,
            connection.UserId,
            connection.ClientType,
            IsOnline: true,
            DateTimeOffset.UtcNow));

        // 发布事件（fire-and-forget，不影响主流程）
        try
        {
            _ = _eventBus?.PublishAsync(new GatewayClientConnectedEvent
            {
                ConnectionId = connection.ConnectionId,
                UserId = connection.UserId,
                ClientType = connection.ClientType
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish GatewayClientConnectedEvent for connectionId={ConnectionId}", connection.ConnectionId);
        }
    }

    /// <inheritdoc />
    public void RemoveConnection(string connectionId)
    {
        Check.NotNullOrWhiteSpace(connectionId);

        if (!_connections.TryRemove(connectionId, out var removed))
        {
            return;
        }

        OnPresenceChanged?.Invoke(new PresenceChangedEvent(
            removed.ConnectionId,
            removed.UserId,
            removed.ClientType,
            IsOnline: false,
            DateTimeOffset.UtcNow));

        // 发布事件（fire-and-forget，不影响主流程）
        try
        {
            _ = _eventBus?.PublishAsync(new GatewayClientDisconnectedEvent
            {
                ConnectionId = removed.ConnectionId,
                Reason = "Connection removed"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish GatewayClientDisconnectedEvent for connectionId={ConnectionId}", removed.ConnectionId);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<GatewayConnectionInfo> GetConnections()
    {
        return _connections.Values.ToList().AsReadOnly();
    }
}
