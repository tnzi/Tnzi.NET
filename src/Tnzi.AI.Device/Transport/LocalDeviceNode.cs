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
        new("system", ["run", "info"], DevicePermissionLevel.Elevated)
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
            "run" => RunCommandAsync(command, cancellationToken),
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

    private static async Task<DeviceCommandResult> RunCommandAsync(DeviceCommand command, CancellationToken cancellationToken)
    {
        var cmd = command.Parameters?.GetProperty("command").GetString();
        if (string.IsNullOrEmpty(cmd))
        {
            return DeviceCommandResult.Fail("Command parameter is required for system run");
        }

        // Use ArgumentList instead of Arguments to prevent shell injection
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd" : "/bin/bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(cmd);
        }
        else
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(cmd);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(command.Timeout);

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            await Task.WhenAll(stdoutTask, stderrTask);
            var stdout = stdoutTask.Result;
            var stderr = stderrTask.Result;
            await process.WaitForExitAsync(cts.Token);

            var output = new StringBuilder();
            if (!string.IsNullOrEmpty(stdout)) output.AppendLine(stdout.TrimEnd());
            if (!string.IsNullOrEmpty(stderr)) output.AppendLine($"[stderr] {stderr.TrimEnd()}");
            output.AppendLine($"[exit_code] {process.ExitCode}");

            return DeviceCommandResult.Ok(output.ToString());
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return DeviceCommandResult.Fail($"Command timed out after {command.Timeout.TotalSeconds}s");
        }
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
