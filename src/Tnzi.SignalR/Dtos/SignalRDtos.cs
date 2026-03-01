namespace Tnzi.SignalR.Dtos;

/// <summary>
/// SignalR connection statistics DTO
/// </summary>
public class SignalRStatsDto
{
    /// <summary>
    /// Number of online users
    /// </summary>
    public int OnlineUserCount { get; set; }

    /// <summary>
    /// Total active connections (across all users)
    /// </summary>
    public int TotalConnectionCount { get; set; }

    /// <summary>
    /// Server time when stats were captured
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Online user information DTO
/// </summary>
public class OnlineUserDto
{
    /// <summary>
    /// User ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Number of active connections for this user
    /// </summary>
    public int ConnectionCount { get; set; }

    /// <summary>
    /// Connection details for this user
    /// </summary>
    public List<ConnectionInfoDto> Connections { get; set; } = new();
}

/// <summary>
/// Connection information DTO
/// </summary>
public class ConnectionInfoDto
{
    /// <summary>
    /// Connection ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;

    /// <summary>
    /// User ID
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// User name
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Connection established time
    /// </summary>
    public DateTime? ConnectedAt { get; set; }

    /// <summary>
    /// Client IP address
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// Client User-Agent
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Hub type name
    /// </summary>
    public string? HubName { get; set; }

    /// <summary>
    /// Groups this connection belongs to
    /// </summary>
    public List<string> Groups { get; set; } = new();
}
