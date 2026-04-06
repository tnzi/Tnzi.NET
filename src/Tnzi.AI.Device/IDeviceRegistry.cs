namespace Tnzi.AI.Device;

/// <summary>
/// Registry for managing connected device nodes
/// </summary>
[ExperimentalApi(Reason = "Gateway/Device/Workspace API under active development")]
public interface IDeviceRegistry
{
    /// <summary>
    /// Get all currently connected nodes
    /// </summary>
    IReadOnlyList<IDeviceNode> GetConnectedNodes();

    /// <summary>
    /// Get a specific node by ID
    /// </summary>
    IDeviceNode? GetNode(string nodeId);

    /// <summary>
    /// Find the first connected node that supports the given capability family
    /// </summary>
    IDeviceNode? FindNodeByCapability(string family);

    /// <summary>
    /// Register a device node
    /// </summary>
    void Register(IDeviceNode node);

    /// <summary>
    /// Unregister a device node
    /// </summary>
    bool Unregister(string nodeId);

    /// <summary>
    /// Raised when a node is registered or unregistered
    /// </summary>
    event Action<DeviceNodeEvent>? OnNodeChanged;
}
