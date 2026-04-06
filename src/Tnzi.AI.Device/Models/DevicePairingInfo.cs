namespace Tnzi.AI.Device.Models;

/// <summary>
/// Information about a paired device
/// </summary>
public class DevicePairingInfo
{
    /// <summary>
    /// Device node ID
    /// </summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable device name
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Device platform
    /// </summary>
    public DevicePlatform Platform { get; init; }

    /// <summary>
    /// Capabilities reported by the device
    /// </summary>
    public IReadOnlyList<DeviceCapability> Capabilities { get; init; } = [];
}
