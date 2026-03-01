namespace Tnzi.SignalR.Services;

/// <summary>
/// Connection metadata for tracking connection details
/// </summary>
public class ConnectionMetadata
{
    /// <summary>
    /// Connection ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// User ID associated with this connection
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// User name associated with this connection
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Connection established time
    /// </summary>
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Client IP address
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// Client User-Agent string
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Hub type name this connection is associated with
    /// </summary>
    public string? HubName { get; set; }

    /// <summary>
    /// Custom properties for extensibility
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}
