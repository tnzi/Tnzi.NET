using Tnzi.AI.Device.Events;
using Tnzi.EventBus;

namespace Tnzi.AI.Device.Services;

/// <summary>
/// In-memory device node registry with thread-safe operations
/// </summary>
public class DeviceRegistry : IDeviceRegistry
{
    private readonly ConcurrentDictionary<string, IDeviceNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<DeviceRegistry> _logger;
    private readonly IEventBus? _eventBus;

    public event Action<DeviceNodeEvent>? OnNodeChanged;

    public DeviceRegistry(ILogger<DeviceRegistry> logger, IEventBus? eventBus = null)
    {
        _logger = Check.NotNull(logger);
        _eventBus = eventBus;
    }

    public IReadOnlyList<IDeviceNode> GetConnectedNodes()
    {
        return _nodes.Values
            .Where(n => n.State == DeviceConnectionState.Connected)
            .ToList()
            .AsReadOnly();
    }

    public IDeviceNode? GetNode(string nodeId)
    {
        Check.NotNullOrWhiteSpace(nodeId);
        return _nodes.GetValueOrDefault(nodeId);
    }

    public IDeviceNode? FindNodeByCapability(string family)
    {
        Check.NotNullOrWhiteSpace(family);
        // 按 NodeId 排序确保确定性选择 ("local" 节点优先于远程节点)
        return _nodes.Values
            .Where(n => n.State == DeviceConnectionState.Connected &&
                        n.Capabilities.Any(c => c.Family.Equals(family, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(n => n.NodeId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public void Register(IDeviceNode node)
    {
        Check.NotNull(node);
        _nodes[node.NodeId] = node;

        _logger.LogInformation("Device node registered: {NodeId} ({Name}, {Platform})",
            node.NodeId, node.Name, node.Platform);

        OnNodeChanged?.Invoke(new DeviceNodeEvent(node.NodeId, DeviceNodeEvent.Registered, node.Platform));

        // 发布事件（fire-and-forget，不影响主流程）
        try
        {
            _ = _eventBus?.PublishAsync(new DeviceNodeRegisteredEvent
            {
                NodeId = node.NodeId,
                Name = node.Name,
                Platform = node.Platform
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish DeviceNodeRegisteredEvent for nodeId={NodeId}", node.NodeId);
        }
    }

    public bool Unregister(string nodeId)
    {
        Check.NotNullOrWhiteSpace(nodeId);

        if (!_nodes.TryRemove(nodeId, out var node))
        {
            return false;
        }

        _logger.LogInformation("Device node unregistered: {NodeId}", nodeId);
        OnNodeChanged?.Invoke(new DeviceNodeEvent(nodeId, DeviceNodeEvent.Unregistered, node.Platform));

        // 发布事件（fire-and-forget，不影响主流程）
        try
        {
            _ = _eventBus?.PublishAsync(new DeviceNodeDisconnectedEvent
            {
                NodeId = nodeId,
                Reason = "Unregistered"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish DeviceNodeDisconnectedEvent for nodeId={NodeId}", nodeId);
        }

        return true;
    }
}
