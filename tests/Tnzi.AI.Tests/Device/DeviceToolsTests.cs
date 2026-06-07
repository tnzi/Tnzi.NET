using Tnzi.AI.Device;
using Tnzi.AI.Device.Models;
using Tnzi.AI.Device.Services;
using Tnzi.AI.Device.Tools;
using Tnzi.AI.Device.Transport;

namespace Tnzi.AI.Tests.Device;

public class DeviceToolsTests
{
    private readonly DeviceRegistry _registry;
    private readonly DeviceTools _tools;

    public DeviceToolsTests()
    {
        _registry = new DeviceRegistry(NullLogger<DeviceRegistry>.Instance);
        _tools = new DeviceTools(_registry);
    }

    [Fact]
    public async Task ListDevices_ReturnsConnectedNodes()
    {
        var node = CreateMockNode("node-1", "clipboard");
        _registry.Register(node);

        var result = await _tools.ListDevicesAsync();

        result.ShouldContain("node-1");
        result.ShouldContain("clipboard");
    }

    [Fact]
    public async Task ListDevices_EmptyRegistry_ReturnsNoDevices()
    {
        var result = await _tools.ListDevicesAsync();

        result.ShouldContain("No connected devices");
    }

    [Fact]
    public async Task Screenshot_NoNodeId_AutoSelects()
    {
        var expectedResult = DeviceCommandResult.Ok("screenshot data", null, "image/png");
        var node = CreateMockNode("node-1", "screenshot", expectedResult);
        _registry.Register(node);

        var result = await _tools.ScreenshotAsync();

        result.Success.ShouldBeTrue();
        result.TextResult.ShouldBe("screenshot data");
    }

    [Fact]
    public async Task Screenshot_NoCapableDevice_ReturnsError()
    {
        var result = await _tools.ScreenshotAsync();

        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("No device with screenshot capability found");
    }

    [Fact]
    public async Task ClipboardRead_DelegatesToNode()
    {
        var expectedResult = DeviceCommandResult.Ok("clipboard text");
        var mockNode = new Mock<IDeviceNode>();
        mockNode.Setup(n => n.NodeId).Returns("node-1");
        mockNode.Setup(n => n.Name).Returns("Test");
        mockNode.Setup(n => n.Platform).Returns(DevicePlatform.Windows);
        mockNode.Setup(n => n.State).Returns(DeviceConnectionState.Connected);
        mockNode.Setup(n => n.Capabilities).Returns(new List<DeviceCapability>
        {
            new("clipboard", ["read", "write"])
        }.AsReadOnly());
        mockNode.Setup(n => n.InvokeAsync(It.Is<DeviceCommand>(c => c.Family == "clipboard" && c.Command == "read"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        _registry.Register(mockNode.Object);

        var result = await _tools.ClipboardReadAsync();

        result.Success.ShouldBeTrue();
        result.TextResult.ShouldBe("clipboard text");
        mockNode.Verify(n => n.InvokeAsync(
            It.Is<DeviceCommand>(c => c.Family == "clipboard" && c.Command == "read"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunCommand_DelegatesToNode()
    {
        var expectedResult = DeviceCommandResult.Ok("command output");
        var mockNode = new Mock<IDeviceNode>();
        mockNode.Setup(n => n.NodeId).Returns("node-1");
        mockNode.Setup(n => n.Name).Returns("Test");
        mockNode.Setup(n => n.Platform).Returns(DevicePlatform.Windows);
        mockNode.Setup(n => n.State).Returns(DeviceConnectionState.Connected);
        mockNode.Setup(n => n.Capabilities).Returns(new List<DeviceCapability>
        {
            new("system", ["run", "info"])
        }.AsReadOnly());
        mockNode.Setup(n => n.InvokeAsync(It.Is<DeviceCommand>(c => c.Family == "system" && c.Command == "run"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        _registry.Register(mockNode.Object);

        var result = await _tools.RunCommandAsync("echo hello");

        result.Success.ShouldBeTrue();
        result.TextResult.ShouldBe("command output");
    }

    [Fact]
    public async Task RunCommand_RealLocalNodeOnly_RejectsWithSandboxGuidance()
    {
        // Real runtime path (no mock): the local node must NOT execute a host shell —
        // device_run resolves to LocalDeviceNode and returns an honest RCE-avoidance
        // failure pointing at the Sandbox module instead of running the command.
        _registry.Register(new LocalDeviceNode());

        var result = await _tools.RunCommandAsync("echo pwned");

        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error!.ShouldContain("Sandbox");
    }

    [Fact]
    public async Task ClipboardWrite_NoDevice_ReturnsError()
    {
        var result = await _tools.ClipboardWriteAsync("test");

        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("No device with clipboard capability found");
    }

    private static IDeviceNode CreateMockNode(string nodeId, string capabilityFamily, DeviceCommandResult? invokeResult = null)
    {
        var mock = new Mock<IDeviceNode>();
        mock.Setup(n => n.NodeId).Returns(nodeId);
        mock.Setup(n => n.Name).Returns($"Test Device {nodeId}");
        mock.Setup(n => n.Platform).Returns(DevicePlatform.Windows);
        mock.Setup(n => n.State).Returns(DeviceConnectionState.Connected);
        mock.Setup(n => n.Capabilities).Returns(new List<DeviceCapability>
        {
            new(capabilityFamily, ["capture", "read", "write"])
        }.AsReadOnly());

        if (invokeResult != null)
        {
            mock.Setup(n => n.InvokeAsync(It.IsAny<DeviceCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(invokeResult);
        }

        return mock.Object;
    }
}
