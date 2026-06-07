namespace Tnzi.AI.Device.Models;

/// <summary>
/// A pending device pairing request (internal — holds the secret Code)
/// </summary>
public class PairingRequest
{
    /// <summary>
    /// Unique request ID — public, used by admins to approve/reject
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Pairing code displayed to the device user — secret, device-side only; must not appear in API list responses
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Device node ID
    /// </summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable device name
    /// </summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>
    /// Device platform
    /// </summary>
    public DevicePlatform Platform { get; init; }

    /// <summary>
    /// When the request was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the pairing code expires
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Request body for admin-initiated device pairing. The returned <see cref="PairingRequest.Code"/>
/// is handed out-of-band to the device, which then connects to the device WebSocket endpoint
/// (default <c>/ws/device</c>) and presents the code to complete registration.
/// </summary>
public class CreatePairingRequestDto
{
    /// <summary>Device node ID to pair</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Human-readable device name</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Device platform</summary>
    public DevicePlatform Platform { get; set; }
}

/// <summary>
/// Public DTO for a pending pairing request returned by the admin API — Code is redacted
/// </summary>
public class PendingPairingRequestDto
{
    /// <summary>
    /// Unique request ID — pass this to approve/reject endpoints
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Device node ID
    /// </summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable device name
    /// </summary>
    public string DeviceName { get; init; } = string.Empty;

    /// <summary>
    /// Device platform
    /// </summary>
    public DevicePlatform Platform { get; init; }

    /// <summary>
    /// When the request was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the pairing code expires
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }
}
