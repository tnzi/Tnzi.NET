using Tnzi.EventBus;

namespace Tnzi.AI.Device.Events;

/// <summary>
/// Raised when a device node is registered
/// </summary>
public class DeviceNodeRegisteredEvent : EventBase
{
    public string NodeId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DevicePlatform Platform { get; init; }
}

/// <summary>
/// Raised when a device node is disconnected/unregistered
/// </summary>
public class DeviceNodeDisconnectedEvent : EventBase
{
    public string NodeId { get; init; } = string.Empty;
    public string? Reason { get; init; }
}

/// <summary>
/// Raised when a pairing request is created
/// </summary>
public class DevicePairingRequestedEvent : EventBase
{
    public string NodeId { get; init; } = string.Empty;
    public string PairingCode { get; init; } = string.Empty;
}

/// <summary>
/// Raised when a pairing request is approved
/// </summary>
public class DevicePairingApprovedEvent : EventBase
{
    public string NodeId { get; init; } = string.Empty;
}
