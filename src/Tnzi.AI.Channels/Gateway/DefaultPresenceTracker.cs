using Tnzi.AI.Channels.Gateway.Events;

namespace Tnzi.AI.Channels.Gateway;

/// <summary>
/// 默认在线状态追踪器 - ConcurrentDictionary 实现，线程安全
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

        PublishInBackground(new GatewayClientConnectedEvent
        {
            ConnectionId = connection.ConnectionId,
            UserId = connection.UserId,
            ClientType = connection.ClientType
        }, connection.ConnectionId);
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

        PublishInBackground(new GatewayClientDisconnectedEvent
        {
            ConnectionId = removed.ConnectionId,
            Reason = "Connection removed"
        }, removed.ConnectionId);
    }

    /// <summary>
    /// 发布事件（fire-and-forget，不影响主流程）。追踪接口是同步的，等不了发布；
    /// 但异步失败也要记下来：此前直接丢弃返回的任务，发布在异步阶段的失败没有任何痕迹。
    /// </summary>
    private void PublishInBackground<TEvent>(TEvent @event, string connectionId) where TEvent : class, IEvent
    {
        Task? publish;
        try
        {
            publish = _eventBus?.PublishAsync(@event);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish {EventType} for connectionId={ConnectionId}", typeof(TEvent).Name, connectionId);
            return;
        }

        publish?.ContinueWith(
            task => _logger.LogWarning(task.Exception, "Failed to publish {EventType} for connectionId={ConnectionId}",
                typeof(TEvent).Name, connectionId),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <inheritdoc />
    public IReadOnlyList<GatewayConnectionInfo> GetConnections()
    {
        return _connections.Values.ToList().AsReadOnly();
    }
}
