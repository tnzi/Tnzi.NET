namespace Tnzi.AI.Device.Tools;

/// <summary>
/// AI tools for interacting with connected device nodes
/// </summary>
[AIToolGroup("device", "Device Tools", "Interact with connected device nodes (clipboard, notifications, system commands)")]
public class DeviceTools : IAIToolProvider
{
    private readonly IDeviceRegistry _registry;

    public DeviceTools(IDeviceRegistry registry)
    {
        _registry = Check.NotNull(registry);
    }

    /// <summary>
    /// List all connected device nodes and their capabilities
    /// </summary>
    [AIFunction("device_list", "List all connected device nodes and their capabilities",
        IsReadOnly = true, IsConcurrencySafe = true)]
    public Task<string> ListDevicesAsync()
    {
        var nodes = _registry.GetConnectedNodes();
        if (nodes.Count == 0)
        {
            return Task.FromResult("No connected devices");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Connected devices ({nodes.Count}):");
        foreach (var node in nodes)
        {
            sb.AppendLine($"  - {node.NodeId}: {node.Name} ({node.Platform})");
            foreach (var cap in node.Capabilities)
            {
                sb.AppendLine($"    [{cap.Family}] commands: {string.Join(", ", cap.Commands)} (level: {cap.RequiredLevel})");
            }
        }

        return Task.FromResult(sb.ToString());
    }

    /// <summary>
    /// Take a screenshot on a device node
    /// </summary>
    [AIFunction("device_screenshot", "Take a screenshot on a device node")]
    public async Task<DeviceCommandResult> ScreenshotAsync(
        [AIParameter("node_id", "Target device node ID (auto-selects if omitted)", false)] string? nodeId = null,
        [AIParameter("region", "Screenshot region: fullscreen or custom region", false)] string? region = null)
    {
        var node = ResolveNode(nodeId, "screenshot");
        if (node == null)
        {
            return DeviceCommandResult.Fail("No device with screenshot capability found");
        }

        var parameters = JsonSerializer.SerializeToElement(new { region = region ?? "fullscreen" });
        var command = new DeviceCommand("screenshot", "capture", parameters);
        return await node.InvokeAsync(command);
    }

    /// <summary>
    /// Read clipboard content from a device node
    /// </summary>
    [AIFunction("device_clipboard_read", "Read clipboard content from a device node",
        IsReadOnly = true)]
    public async Task<DeviceCommandResult> ClipboardReadAsync(
        [AIParameter("node_id", "Target device node ID (auto-selects if omitted)", false)] string? nodeId = null)
    {
        var node = ResolveNode(nodeId, "clipboard");
        if (node == null)
        {
            return DeviceCommandResult.Fail("No device with clipboard capability found");
        }

        var command = new DeviceCommand("clipboard", "read");
        return await node.InvokeAsync(command);
    }

    /// <summary>
    /// Write content to clipboard on a device node
    /// </summary>
    [AIFunction("device_clipboard_write", "Write content to clipboard on a device node")]
    public async Task<DeviceCommandResult> ClipboardWriteAsync(
        [AIParameter("content", "Text content to write to clipboard")] string content,
        [AIParameter("node_id", "Target device node ID (auto-selects if omitted)", false)] string? nodeId = null)
    {
        var node = ResolveNode(nodeId, "clipboard");
        if (node == null)
        {
            return DeviceCommandResult.Fail("No device with clipboard capability found");
        }

        var parameters = JsonSerializer.SerializeToElement(new { content });
        var command = new DeviceCommand("clipboard", "write", parameters);
        return await node.InvokeAsync(command);
    }

    /// <summary>
    /// Send a notification to a device node
    /// </summary>
    [AIFunction("device_notify", "Send a notification to a device node")]
    public async Task<DeviceCommandResult> NotifyAsync(
        [AIParameter("title", "Notification title")] string title,
        [AIParameter("body", "Notification body text")] string body,
        [AIParameter("node_id", "Target device node ID (auto-selects if omitted)", false)] string? nodeId = null)
    {
        var node = ResolveNode(nodeId, "notification");
        if (node == null)
        {
            return DeviceCommandResult.Fail("No device with notification capability found");
        }

        var parameters = JsonSerializer.SerializeToElement(new { title, body });
        var command = new DeviceCommand("notification", "send", parameters);
        return await node.InvokeAsync(command);
    }

    /// <summary>
    /// Run a system command on a target device node. The command is delivered to the chosen
    /// node, which executes it under its own security policy. The local server node does NOT
    /// execute host shell commands (RCE risk) — for isolated execution on the server, load the
    /// Sandbox module and use its bash tool.
    /// </summary>
    [AIFunction("device_run", "Run a system command on a target device node. The command is delivered to the chosen device, which executes it under its own security policy. The local server node does NOT run host shell commands — for isolated execution on the server, use the Sandbox module's bash tool.",
        SearchHint = "execute run command device")]
    public async Task<DeviceCommandResult> RunCommandAsync(
        [AIParameter("command", "System command to execute")] string command,
        [AIParameter("node_id", "Target device node ID (auto-selects if omitted)", false)] string? nodeId = null)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return DeviceCommandResult.Fail("Command is required");
        }

        var node = ResolveNode(nodeId, "system");
        if (node == null)
        {
            return DeviceCommandResult.Fail("No device with system capability found");
        }

        var parameters = JsonSerializer.SerializeToElement(new { command });
        var cmd = new DeviceCommand("system", "run", parameters);
        return await node.InvokeAsync(cmd);
    }

    /// <summary>
    /// Resolve target node: by explicit ID or auto-select by capability family
    /// </summary>
    private IDeviceNode? ResolveNode(string? nodeId, string family)
    {
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            return _registry.GetNode(nodeId);
        }

        return _registry.FindNodeByCapability(family);
    }
}
