using System.Runtime.InteropServices;
using TextCopy;

namespace Tnzi.AI.Device.Transport;

/// <summary>
/// Local device node — represents the current machine as a device node
/// </summary>
public class LocalDeviceNode : IDeviceNode
{
    public string NodeId => "local";

    public string Name => Environment.MachineName;

    public DevicePlatform Platform { get; } = DetectPlatform();

    public DeviceConnectionState State => DeviceConnectionState.Connected;

    public IReadOnlyList<DeviceCapability> Capabilities { get; } =
    [
        new("clipboard", ["read", "write"]),
        new("notification", ["send"]),
        new("system", ["info"], DevicePermissionLevel.Elevated)
        // "run" (host shell execution) is intentionally NOT offered by the local node —
        // running arbitrary model-supplied commands on the server host is an RCE. Isolated
        // command execution belongs to the Sandbox module (Docker/Local sandbox); remote
        // device nodes may still implement "system/run" on their own side.
        // screenshot and file families not yet implemented — add when InvokeAsync handles them
    ];

    public async Task<DeviceCommandResult> InvokeAsync(DeviceCommand command, CancellationToken cancellationToken = default)
    {
        Check.NotNull(command);

        try
        {
            return command.Family.ToLowerInvariant() switch
            {
                "clipboard" => await HandleClipboardAsync(command, cancellationToken),
                "system" => await HandleSystemAsync(command, cancellationToken),
                "notification" => HandleNotification(command),
                _ => DeviceCommandResult.Fail($"Unsupported command family: {command.Family}")
            };
        }
        catch (OperationCanceledException)
        {
            return DeviceCommandResult.Fail("Command was cancelled");
        }
        catch (Exception ex)
        {
            return DeviceCommandResult.Fail($"Command execution failed: {ex.Message}");
        }
    }

    private static async Task<DeviceCommandResult> HandleClipboardAsync(DeviceCommand command, CancellationToken cancellationToken)
    {
        var clipboard = new Clipboard();

        return command.Command.ToLowerInvariant() switch
        {
            "read" => DeviceCommandResult.Ok(await clipboard.GetTextAsync(cancellationToken) ?? string.Empty),
            "write" => await WriteClipboardAsync(clipboard, command, cancellationToken),
            _ => DeviceCommandResult.Fail($"Unknown clipboard command: {command.Command}")
        };
    }

    private static async Task<DeviceCommandResult> WriteClipboardAsync(Clipboard clipboard, DeviceCommand command, CancellationToken cancellationToken)
    {
        var content = command.Parameters?.GetProperty("content").GetString();
        if (string.IsNullOrEmpty(content))
        {
            return DeviceCommandResult.Fail("Content parameter is required for clipboard write");
        }

        await clipboard.SetTextAsync(content, cancellationToken);
        return DeviceCommandResult.Ok("Clipboard updated");
    }

    private static Task<DeviceCommandResult> HandleSystemAsync(DeviceCommand command, CancellationToken cancellationToken)
    {
        return command.Command.ToLowerInvariant() switch
        {
            "info" => Task.FromResult(GetSystemInfo()),
            "run" => Task.FromResult(RunNotSupported()),
            _ => Task.FromResult(DeviceCommandResult.Fail($"Unknown system command: {command.Command}"))
        };
    }

    private static DeviceCommandResult GetSystemInfo()
    {
        var info = new StringBuilder();
        info.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        info.AppendLine($"Machine: {Environment.MachineName}");
        info.AppendLine($"Processors: {Environment.ProcessorCount}");
        info.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        info.AppendLine($"Architecture: {RuntimeInformation.OSArchitecture}");

        return DeviceCommandResult.Ok(info.ToString());
    }

    /// <summary>
    /// Host shell execution via the local device node is deliberately disabled: running
    /// arbitrary model-supplied commands on the server host is a remote-code-execution
    /// risk, and a token/substring denylist is not a real boundary. For isolated command
    /// execution on the server, load the Sandbox module and use its <c>bash</c> tool
    /// (Docker/Local sandbox with resource limits and path virtualization).
    /// </summary>
    private static DeviceCommandResult RunNotSupported()
    {
        return DeviceCommandResult.Fail(
            "Host shell execution is not available on the local server node (RCE risk). " +
            "Load the Sandbox module and use its isolated 'bash' tool for command execution.");
    }

    private static DeviceCommandResult HandleNotification(DeviceCommand _)
    {
        // The local server node runs headless — there is no desktop session to send a toast/notification to.
        // Return an honest failure rather than silently pretending to succeed.
        return DeviceCommandResult.Fail("Notification not supported on the local server node (headless environment)");
    }

    private static DevicePlatform DetectPlatform()
    {
        if (OperatingSystem.IsWindows()) return DevicePlatform.Windows;
        if (OperatingSystem.IsMacOS()) return DevicePlatform.MacOS;
        if (OperatingSystem.IsLinux()) return DevicePlatform.Linux;
        return DevicePlatform.Unknown;
    }
}
