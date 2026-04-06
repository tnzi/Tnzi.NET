namespace Tnzi.AI.Device.Models;

/// <summary>
/// Describes a device capability family and its available commands
/// </summary>
public class DeviceCapability
{
    /// <summary>
    /// Capability family name (e.g. "clipboard", "screenshot", "notification")
    /// </summary>
    public string Family { get; }

    /// <summary>
    /// Available commands within this capability family
    /// </summary>
    public IReadOnlyList<string> Commands { get; }

    /// <summary>
    /// Required permission level to invoke this capability
    /// </summary>
    public DevicePermissionLevel RequiredLevel { get; }

    public DeviceCapability(string family, IReadOnlyList<string> commands, DevicePermissionLevel requiredLevel = DevicePermissionLevel.Basic)
    {
        Family = Check.NotNullOrWhiteSpace(family);
        Commands = Check.NotNull(commands);
        RequiredLevel = requiredLevel;
    }
}
