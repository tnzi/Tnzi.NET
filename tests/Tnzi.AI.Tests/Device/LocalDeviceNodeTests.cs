using Tnzi.AI.Device;
using Tnzi.AI.Device.Models;
using Tnzi.AI.Device.Transport;

namespace Tnzi.AI.Tests.Device;

public class LocalDeviceNodeTests
{
    private readonly LocalDeviceNode _node;

    public LocalDeviceNodeTests()
    {
        _node = new LocalDeviceNode();
    }

    [Fact]
    public void Properties_ReturnLocalValues()
    {
        _node.NodeId.ShouldBe("local");
        _node.Name.ShouldBe(Environment.MachineName);
        _node.State.ShouldBe(DeviceConnectionState.Connected);
        _node.Capabilities.ShouldNotBeEmpty();
        _node.Capabilities.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Platform_IsDetected()
    {
        // 应检测到当前平台，不应为 Unknown
        if (OperatingSystem.IsWindows())
            _node.Platform.ShouldBe(DevicePlatform.Windows);
        else if (OperatingSystem.IsMacOS())
            _node.Platform.ShouldBe(DevicePlatform.MacOS);
        else if (OperatingSystem.IsLinux())
            _node.Platform.ShouldBe(DevicePlatform.Linux);
    }

    [Fact]
    public async Task InvokeAsync_SystemInfo_ReturnsText()
    {
        var command = new DeviceCommand("system", "info");

        var result = await _node.InvokeAsync(command);

        result.Success.ShouldBeTrue();
        result.TextResult.ShouldNotBeNullOrWhiteSpace();
        result.TextResult.ShouldContain("OS");
        result.TextResult.ShouldContain("Machine");
    }

    [Fact]
    public async Task InvokeAsync_UnknownFamily_ReturnsError()
    {
        var command = new DeviceCommand("unknown_family", "test");

        var result = await _node.InvokeAsync(command);

        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("Unsupported command family");
    }

    [Fact]
    public async Task InvokeAsync_Notification_ReturnsUnsupportedFail()
    {
        // The local server node is headless — notification must return an honest failure, not pretend to succeed
        var parameters = JsonSerializer.SerializeToElement(new { title = "Test", body = "Hello" });
        var command = new DeviceCommand("notification", "send", parameters);

        var result = await _node.InvokeAsync(command);

        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("not supported");
    }

    [Fact]
    public async Task InvokeAsync_ClipboardWrite_BestEffort()
    {
        // Clipboard operations may fail on CI without a display; best-effort test
        var parameters = JsonSerializer.SerializeToElement(new { content = "test clipboard content" });
        var command = new DeviceCommand("clipboard", "write", parameters);

        var result = await _node.InvokeAsync(command);

        // 不论成功失败，不应抛出异常
        result.ShouldNotBeNull();
    }
}
