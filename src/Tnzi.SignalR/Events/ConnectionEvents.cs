
namespace Tnzi.SignalR.Events;

/// <summary>
/// Event raised when a user connects to a SignalR hub
/// </summary>
public class UserConnectedEvent : EventBase
{
    /// <summary>
    /// User ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Connection ID assigned by SignalR
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Hub type name
    /// </summary>
    public string HubName { get; set; } = string.Empty;

    /// <summary>
    /// User name (if available)
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Client IP address (if available)
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// Total active connections for this user after connecting
    /// </summary>
    public int TotalConnections { get; set; }
}

/// <summary>
/// Event raised when a user disconnects from a SignalR hub
/// </summary>
public class UserDisconnectedEvent : EventBase
{
    /// <summary>
    /// User ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Connection ID that was disconnected
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// Hub type name
    /// </summary>
    public string HubName { get; set; } = string.Empty;

    /// <summary>
    /// Disconnect reason (exception message, if any)
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Remaining active connections for this user after disconnecting
    /// </summary>
    public int RemainingConnections { get; set; }

    /// <summary>
    /// Whether the user went fully offline (no remaining connections)
    /// </summary>
    public bool WentOffline { get; set; }
}
