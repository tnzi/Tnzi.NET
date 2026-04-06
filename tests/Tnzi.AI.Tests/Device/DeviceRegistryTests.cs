using Tnzi.AI.Device;
using Tnzi.AI.Device.Models;
using Tnzi.AI.Device.Services;

namespace Tnzi.AI.Tests.Device;

public class DeviceRegistryTests
{
    private readonly DeviceRegistry _registry;

    public DeviceRegistryTests()
    {
        _registry = new DeviceRegistry(NullLogger<DeviceRegistry>.Instance);
    }

    [Fact]
    public void Register_AddsNode()
    {
        var node = CreateMockNode("node-1");

        _registry.Register(node);

        _registry.GetNode("node-1").ShouldNotBeNull();
        _registry.GetConnectedNodes().Count.ShouldBe(1);
    }

    [Fact]
    public void Unregister_RemovesNode()
    {
        var node = CreateMockNode("node-1");
        _registry.Register(node);

        var result = _registry.Unregister("node-1");

        result.ShouldBeTrue();
        _registry.GetNode("node-1").ShouldBeNull();
        _registry.GetConnectedNodes().Count.ShouldBe(0);
    }

    [Fact]
    public void GetNode_ExistingId_ReturnsNode()
    {
        var node = CreateMockNode("test-node");
        _registry.Register(node);

        var result = _registry.GetNode("test-node");

        result.ShouldNotBeNull();
        result.NodeId.ShouldBe("test-node");
    }

    [Fact]
    public void GetNode_NonExistent_ReturnsNull()
    {
        var result = _registry.GetNode("nonexistent");

        result.ShouldBeNull();
    }

    [Fact]
    public void FindNodeByCapability_Matches()
    {
        var node = CreateMockNode("node-1", "clipboard");
        _registry.Register(node);

        var result = _registry.FindNodeByCapability("clipboard");

        result.ShouldNotBeNull();
        result.NodeId.ShouldBe("node-1");
    }

    [Fact]
    public void FindNodeByCapability_CaseInsensitive()
    {
        var node = CreateMockNode("node-1", "Clipboard");
        _registry.Register(node);

        var result = _registry.FindNodeByCapability("CLIPBOARD");

        result.ShouldNotBeNull();
    }

    [Fact]
    public void FindNodeByCapability_NoMatch_ReturnsNull()
    {
        var node = CreateMockNode("node-1", "clipboard");
        _registry.Register(node);

        var result = _registry.FindNodeByCapability("screenshot");

        result.ShouldBeNull();
    }

    [Fact]
    public void OnNodeChanged_Fires_OnRegister()
    {
        DeviceNodeEvent? receivedEvent = null;
        _registry.OnNodeChanged += e => receivedEvent = e;

        _registry.Register(CreateMockNode("node-1"));

        receivedEvent.ShouldNotBeNull();
        receivedEvent.NodeId.ShouldBe("node-1");
        receivedEvent.EventType.ShouldBe(DeviceNodeEvent.Registered);
    }

    [Fact]
    public void OnNodeChanged_Fires_OnUnregister()
    {
        _registry.Register(CreateMockNode("node-1"));

        DeviceNodeEvent? receivedEvent = null;
        _registry.OnNodeChanged += e => receivedEvent = e;

        _registry.Unregister("node-1");

        receivedEvent.ShouldNotBeNull();
        receivedEvent.EventType.ShouldBe(DeviceNodeEvent.Unregistered);
    }

    [Fact]
    public void Register_ThreadSafe()
    {
        // 并发注册 50 个节点
        Parallel.For(0, 50, i =>
        {
            _registry.Register(CreateMockNode($"node-{i}"));
        });

        _registry.GetConnectedNodes().Count.ShouldBe(50);
    }

    private static IDeviceNode CreateMockNode(string nodeId, string? capabilityFamily = null)
    {
        var mock = new Mock<IDeviceNode>();
        mock.Setup(n => n.NodeId).Returns(nodeId);
        mock.Setup(n => n.Name).Returns($"Test Device {nodeId}");
        mock.Setup(n => n.Platform).Returns(DevicePlatform.Windows);
        mock.Setup(n => n.State).Returns(DeviceConnectionState.Connected);

        var capabilities = new List<DeviceCapability>();
        if (capabilityFamily != null)
        {
            capabilities.Add(new DeviceCapability(capabilityFamily, ["read", "write"]));
        }

        mock.Setup(n => n.Capabilities).Returns(capabilities.AsReadOnly());
        return mock.Object;
    }
}
