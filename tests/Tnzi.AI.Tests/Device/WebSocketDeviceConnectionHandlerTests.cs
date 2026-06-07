using System.Text.Json;
using Tnzi.AI.Device;
using Tnzi.AI.Device.Models;
using Tnzi.AI.Device.Options;
using Tnzi.AI.Device.Services;
using Tnzi.AI.Device.Transport;

namespace Tnzi.AI.Tests.Device;

/// <summary>
/// Tests for WebSocketDeviceConnectionHandler using in-memory fake channels.
/// Validates the pairing handshake, capability parsing, registry registration,
/// and rejection paths.
/// </summary>
public class WebSocketDeviceConnectionHandlerTests
{
    // ─── Shared fake channel ──────────────────────────────────────────────────

    private sealed class FakeChannel : IDeviceChannel
    {
        private readonly System.Threading.Channels.Channel<string> _toServer =
            System.Threading.Channels.Channel.CreateUnbounded<string>();

        private readonly System.Threading.Channels.Channel<string?> _fromServer =
            System.Threading.Channels.Channel.CreateUnbounded<string?>();

        public bool IsOpen { get; private set; } = true;

        public async Task SendAsync(string json, CancellationToken cancellationToken = default)
        {
            await _fromServer.Writer.WriteAsync(json, cancellationToken);
        }

        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            return await _toServer.Reader.ReadAsync(cancellationToken);
        }

        // Device side helpers
        public async Task DeviceSendAsync(string json) => await _toServer.Writer.WriteAsync(json);
        public async Task<string> DeviceReadAsync(CancellationToken ct = default) =>
            await _fromServer.Reader.ReadAsync(ct);

        public void DeviceClose()
        {
            IsOpen = false;
            _toServer.Writer.TryWrite(null);
            _toServer.Writer.Complete();
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static DevicePairingService CreatePairingService()
    {
        var options = new DeviceOptions { PairingCodeLength = 8, PairingCodeTtlMinutes = 60, MaxPendingPairings = 5 };
        return new DevicePairingService(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<DevicePairingService>.Instance);
    }

    private static WebSocketDeviceConnectionHandler CreateHandler(
        IDevicePairingService pairingService,
        IDeviceRegistry registry)
    {
        return new WebSocketDeviceConnectionHandler(
            pairingService,
            registry,
            NullLoggerFactory.Instance,
            NullLogger<WebSocketDeviceConnectionHandler>.Instance);
    }

    private static DeviceRegistry CreateRegistry() =>
        new DeviceRegistry(NullLogger<DeviceRegistry>.Instance);

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handshake_ValidCode_RegistersNode()
    {
        var pairingService = CreatePairingService();
        var registry = CreateRegistry();
        var handler = CreateHandler(pairingService, registry);
        var channel = new FakeChannel();

        // Create a pairing request so there is a valid code
        var request = await pairingService.CreatePairingRequestAsync("device-ws-1", "My Phone", DevicePlatform.Android);

        // Run handshake in background
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var handshakeTask = handler.RunHandshakeAndReceiveLoopAsync(channel, cts.Token);

        // Device sends register
        var registerMsg = JsonSerializer.Serialize(new
        {
            type = "register",
            pairingCode = request.Code,
            name = "My Phone",
            platform = "android",
            capabilities = new[]
            {
                new { family = "clipboard", commands = new[] { "read", "write" }, permission = "basic" },
                new { family = "notification", commands = new[] { "send" }, permission = "basic" }
            }
        }, JsonOpts);
        await channel.DeviceSendAsync(registerMsg);

        // Device reads the registered ack
        var ack = await channel.DeviceReadAsync(cts.Token);
        using var ackDoc = JsonDocument.Parse(ack);
        ackDoc.RootElement.GetProperty("type").GetString().ShouldBe("registered");
        ackDoc.RootElement.GetProperty("nodeId").GetString().ShouldBe("device-ws-1");

        // Node should be registered
        await Task.Delay(50);
        registry.GetNode("device-ws-1").ShouldNotBeNull();
        registry.GetNode("device-ws-1")!.Name.ShouldBe("My Phone");
        registry.GetNode("device-ws-1")!.Platform.ShouldBe(DevicePlatform.Android);
        registry.GetNode("device-ws-1")!.Capabilities.Count.ShouldBe(2);

        // Close device; handshake loop should finish
        channel.DeviceClose();
        await handshakeTask;
    }

    [Fact]
    public async Task Handshake_InvalidCode_SendsRejected()
    {
        var pairingService = CreatePairingService();
        var registry = CreateRegistry();
        var handler = CreateHandler(pairingService, registry);
        var channel = new FakeChannel();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var handshakeTask = handler.RunHandshakeAndReceiveLoopAsync(channel, cts.Token);

        // Device sends register with wrong code
        await channel.DeviceSendAsync(JsonSerializer.Serialize(new
        {
            type = "register",
            pairingCode = "BADCODE1",
            name = "Rogue Device",
            platform = "android",
            capabilities = Array.Empty<object>()
        }, JsonOpts));

        // Server should send rejected
        var rejected = await channel.DeviceReadAsync(cts.Token);
        using var doc = JsonDocument.Parse(rejected);
        doc.RootElement.GetProperty("type").GetString().ShouldBe("rejected");
        doc.RootElement.GetProperty("reason").GetString().ShouldNotBeNullOrWhiteSpace();

        // Node must NOT be registered
        registry.GetConnectedNodes().Count.ShouldBe(0);

        await handshakeTask;
    }

    [Fact]
    public async Task Handshake_MissingType_SendsRejected()
    {
        var pairingService = CreatePairingService();
        var registry = CreateRegistry();
        var handler = CreateHandler(pairingService, registry);
        var channel = new FakeChannel();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var handshakeTask = handler.RunHandshakeAndReceiveLoopAsync(channel, cts.Token);

        // Device sends malformed message (wrong type)
        await channel.DeviceSendAsync(JsonSerializer.Serialize(new
        {
            type = "hello",
            pairingCode = "ANYCODE1"
        }, JsonOpts));

        var rejected = await channel.DeviceReadAsync(cts.Token);
        using var doc = JsonDocument.Parse(rejected);
        doc.RootElement.GetProperty("type").GetString().ShouldBe("rejected");

        await handshakeTask;
    }

    [Fact]
    public async Task Handshake_NodeUnregistered_OnDisconnect()
    {
        var pairingService = CreatePairingService();
        var registry = CreateRegistry();
        var handler = CreateHandler(pairingService, registry);
        var channel = new FakeChannel();

        var request = await pairingService.CreatePairingRequestAsync("device-ws-2", "Laptop", DevicePlatform.Windows);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var handshakeTask = handler.RunHandshakeAndReceiveLoopAsync(channel, cts.Token);

        await channel.DeviceSendAsync(JsonSerializer.Serialize(new
        {
            type = "register",
            pairingCode = request.Code,
            name = "Laptop",
            platform = "windows",
            capabilities = Array.Empty<object>()
        }, JsonOpts));

        // Consume the ack
        await channel.DeviceReadAsync(cts.Token);
        await Task.Delay(50);

        // Verify node is registered
        registry.GetNode("device-ws-2").ShouldNotBeNull();

        // Device closes connection
        channel.DeviceClose();
        await handshakeTask;

        // Give the Disconnected event handler a moment to fire
        await Task.Delay(100);

        // Node should be unregistered after disconnect
        registry.GetNode("device-ws-2").ShouldBeNull();
    }

    [Fact]
    public async Task Handshake_CapabilityParsing_ElevatedPermission()
    {
        var pairingService = CreatePairingService();
        var registry = CreateRegistry();
        var handler = CreateHandler(pairingService, registry);
        var channel = new FakeChannel();

        var request = await pairingService.CreatePairingRequestAsync("device-ws-3", "Desktop", DevicePlatform.Windows);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var handshakeTask = handler.RunHandshakeAndReceiveLoopAsync(channel, cts.Token);

        await channel.DeviceSendAsync(JsonSerializer.Serialize(new
        {
            type = "register",
            pairingCode = request.Code,
            name = "Desktop",
            platform = "windows",
            capabilities = new[]
            {
                new { family = "system", commands = new[] { "run", "info" }, permission = "elevated" }
            }
        }, JsonOpts));

        await channel.DeviceReadAsync(cts.Token);
        await Task.Delay(50);

        var node = registry.GetNode("device-ws-3");
        node.ShouldNotBeNull();
        var systemCap = node!.Capabilities.Single(c => c.Family == "system");
        systemCap.RequiredLevel.ShouldBe(DevicePermissionLevel.Elevated);
        systemCap.Commands.ShouldContain("run");
        systemCap.Commands.ShouldContain("info");

        channel.DeviceClose();
        await handshakeTask;
    }

    [Fact]
    public async Task Handshake_UnknownPlatform_DefaultsToUnknown()
    {
        var pairingService = CreatePairingService();
        var registry = CreateRegistry();
        var handler = CreateHandler(pairingService, registry);
        var channel = new FakeChannel();

        var request = await pairingService.CreatePairingRequestAsync("device-ws-4", "Mystery Box", DevicePlatform.Unknown);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var handshakeTask = handler.RunHandshakeAndReceiveLoopAsync(channel, cts.Token);

        await channel.DeviceSendAsync(JsonSerializer.Serialize(new
        {
            type = "register",
            pairingCode = request.Code,
            name = "Mystery Box",
            platform = "fuchsia",  // Unknown platform
            capabilities = Array.Empty<object>()
        }, JsonOpts));

        await channel.DeviceReadAsync(cts.Token);
        await Task.Delay(50);

        registry.GetNode("device-ws-4")!.Platform.ShouldBe(DevicePlatform.Unknown);

        channel.DeviceClose();
        await handshakeTask;
    }
}
