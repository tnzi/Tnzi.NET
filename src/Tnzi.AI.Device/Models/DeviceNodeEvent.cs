namespace Tnzi.AI.Device.Models;

/// <summary>
/// Event raised when a device node state changes
/// </summary>
public record DeviceNodeEvent(string NodeId, string EventType, DevicePlatform Platform)
{
    public const string Registered = "registered";
    public const string Unregistered = "unregistered";
}
