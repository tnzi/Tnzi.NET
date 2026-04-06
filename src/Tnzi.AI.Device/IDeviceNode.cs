namespace Tnzi.AI.Device;

/// <summary>
/// Represents a connected device node that can execute commands
/// </summary>
[ExperimentalApi(Reason = "Gateway/Device/Workspace API under active development")]
public interface IDeviceNode
{
    /// <summary>
    /// Unique node identifier
    /// </summary>
    string NodeId { get; }

    /// <summary>
    /// Human-readable device name
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Device platform
    /// </summary>
    DevicePlatform Platform { get; }

    /// <summary>
    /// Capabilities supported by this device
    /// </summary>
    IReadOnlyList<DeviceCapability> Capabilities { get; }

    /// <summary>
    /// Current connection state
    /// </summary>
    DeviceConnectionState State { get; }

    /// <summary>
    /// Execute a command on this device node
    /// </summary>
    Task<DeviceCommandResult> InvokeAsync(DeviceCommand command, CancellationToken cancellationToken = default);
}
