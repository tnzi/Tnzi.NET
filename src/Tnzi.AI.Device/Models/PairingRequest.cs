namespace Tnzi.AI.Device.Models;

/// <summary>
/// A pending device pairing request
/// </summary>
public class PairingRequest
{
    /// <summary>
    /// Unique request ID
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Pairing code displayed to the user
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
