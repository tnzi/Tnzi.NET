namespace Tnzi.AI.Device.Models;

/// <summary>
/// A command to be executed on a device node
/// </summary>
public class DeviceCommand
{
    /// <summary>
    /// Capability family (e.g. "clipboard", "system")
    /// </summary>
    public string Family { get; }

    /// <summary>
    /// Specific command within the family (e.g. "read", "write", "info")
    /// </summary>
    public string Command { get; }

    /// <summary>
    /// Optional parameters for the command
    /// </summary>
    public JsonElement? Parameters { get; }

    /// <summary>
    /// Command execution timeout
    /// </summary>
    public TimeSpan Timeout { get; }

    public DeviceCommand(string family, string command, JsonElement? parameters = null, TimeSpan? timeout = null)
    {
        Family = Check.NotNullOrWhiteSpace(family);
        Command = Check.NotNullOrWhiteSpace(command);
        Parameters = parameters;
        Timeout = timeout ?? TimeSpan.FromSeconds(30);
    }
}
