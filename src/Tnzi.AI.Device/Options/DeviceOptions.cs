namespace Tnzi.AI.Device.Options;

/// <summary>
/// Configuration options for the Device module
/// </summary>
public class DeviceOptions
{
    /// <summary>
    /// Whether the device module is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Length of the pairing code
    /// </summary>
    public int PairingCodeLength { get; set; } = 8;

    /// <summary>
    /// Pairing code time-to-live in minutes
    /// </summary>
    public int PairingCodeTtlMinutes { get; set; } = 60;

    /// <summary>
    /// Maximum number of pending pairing requests
    /// </summary>
    public int MaxPendingPairings { get; set; } = 5;

    /// <summary>
    /// Whether elevated-permission operations require explicit approval
    /// </summary>
    public bool RequireApprovalForElevated { get; set; } = true;

    /// <summary>
    /// List of allowed platforms (null = all platforms allowed)
    /// </summary>
    public List<string>? AllowedPlatforms { get; set; }

    /// <summary>
    /// Whether to register the local device node on startup
    /// </summary>
    public bool LocalNodeEnabled { get; set; } = true;
}
