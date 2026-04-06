namespace Tnzi.AI.Device.Models;

/// <summary>
/// Result of a device command execution
/// </summary>
public class DeviceCommandResult
{
    /// <summary>
    /// Whether the command executed successfully
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Text result (for text-returning commands)
    /// </summary>
    public string? TextResult { get; init; }

    /// <summary>
    /// Binary result (for binary-returning commands like screenshots)
    /// </summary>
    public byte[]? BinaryResult { get; init; }

    /// <summary>
    /// MIME type of the result (e.g. "image/png", "text/plain")
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Error message when Success is false
    /// </summary>
    public string? Error { get; init; }

    public static DeviceCommandResult Ok(string? text = null, byte[]? binary = null, string? mimeType = null) =>
        new() { Success = true, TextResult = text, BinaryResult = binary, MimeType = mimeType };

    public static DeviceCommandResult Fail(string error) =>
        new() { Success = false, Error = error };
}
