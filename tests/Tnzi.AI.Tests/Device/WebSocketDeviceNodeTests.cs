using System.Text.Json;
using Tnzi.AI.Device;
using Tnzi.AI.Device.Models;
using Tnzi.AI.Device.Transport;

namespace Tnzi.AI.Tests.Device;

/// <summary>
/// Tests for WebSocketDeviceNode using an in-memory fake channel.
/// No real WebSocket or network is required.
/// </summary>
public class WebSocketDeviceNodeTests
{
    // ─── Fake channel ──────────────────────────────────────────────────────────

    /// <summary>
    /// Bidirectional in-memory channel: the test controls what the "device" sends back.
    /// </summary>
    private sealed class FakeDeviceChannel : IDeviceChannel
    {
        // Messages the server sends to the device (via IDeviceChannel.SendAsync)
        private readonly System.Threading.Channels.Channel<string> _outbound =
            System.Threading.Channels.Channel.CreateUnbounded<string>();

        // Messages the "device" sends back to the server (via IDeviceChannel.ReceiveAsync)
        private readonly System.Threading.Channels.Channel<string?> _inbound =
            System.Threading.Channels.Channel.CreateUnbounded<string?>();

        public bool IsOpen { get; private set; } = true;

        public async Task SendAsync(string json, CancellationToken cancellationToken = default)
        {
            await _outbound.Writer.WriteAsync(json, cancellationToken);
        }

        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            return await _inbound.Reader.ReadAsync(cancellationToken);
        }

        /// <summary>Simulate the device sending a message to the server.</summary>
        public async Task SimulateDeviceSendAsync(string json)
        {
            await _inbound.Writer.WriteAsync(json);
        }

        /// <summary>Simulate the device closing the connection.</summary>
        public void SimulateClose()
        {
            IsOpen = false;
            _inbound.Writer.TryWrite(null);
            _inbound.Writer.Complete();
        }

        /// <summary>Read the last message the server sent to the device.</summary>
        public async Task<string> ReadServerMessageAsync(CancellationToken cancellationToken = default)
        {
            return await _outbound.Reader.ReadAsync(cancellationToken);
        }
    }

    // ─── Factory ──────────────────────────────────────────────────────────────

    private static WebSocketDeviceNode CreateNode(FakeDeviceChannel channel, string nodeId = "device-1")
    {
        var capabilities = new List<DeviceCapability>
        {
            new("clipboard", ["read", "write"]),
            new("notification", ["send"])
        };

        return new WebSocketDeviceNode(
            nodeId,
            "Test Device",
            DevicePlatform.Windows,
            capabilities.AsReadOnly(),
            channel,
            NullLogger<WebSocketDeviceNode>.Instance);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void Properties_ReflectConstructorArguments()
    {
        var channel = new FakeDeviceChannel();
        var node = CreateNode(channel, "test-node-42");

        node.NodeId.ShouldBe("test-node-42");
        node.Name.ShouldBe("Test Device");
        node.Platform.ShouldBe(DevicePlatform.Windows);
        node.Capabilities.Count.ShouldBe(2);
        node.Capabilities.Any(c => c.Family == "clipboard").ShouldBeTrue();
        node.State.ShouldBe(DeviceConnectionState.Pairing);
    }

    [Fact]
    public void StartReceiveLoop_SetsStateToConnected()
    {
        var channel = new FakeDeviceChannel();
        var node = CreateNode(channel);

        node.StartReceiveLoop();

        node.State.ShouldBe(DeviceConnectionState.Connected);
    }

    [Fact]
    public async Task InvokeAsync_SendsInvokeMessageWithCorrelationId()
    {
        var channel = new FakeDeviceChannel();
        var node = CreateNode(channel);
        node.StartReceiveLoop();

        var command = new DeviceCommand("clipboard", "read", timeout: TimeSpan.FromSeconds(5));

        // Start the invoke (don't await — it waits for a response)
        var invocationTask = node.InvokeAsync(command);

        // Read the message the server sent to the device
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var json = await channel.ReadServerMessageAsync(cts.Token);

        json.ShouldNotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().ShouldBe("invoke");
        root.GetProperty("family").GetString().ShouldBe("clipboard");
        root.GetProperty("command").GetString().ShouldBe("read");

        var correlationId = root.GetProperty("correlationId").GetString();
        correlationId.ShouldNotBeNullOrWhiteSpace();

        // Now simulate the device responding
        var resultJson = JsonSerializer.Serialize(new
        {
            type = "result",
            correlationId,
            success = true,
            textResult = "hello from clipboard"
        }, JsonOpts);
        await channel.SimulateDeviceSendAsync(resultJson);

        var result = await invocationTask;
        result.Success.ShouldBeTrue();
        result.TextResult.ShouldBe("hello from clipboard");
    }

    [Fact]
    public async Task InvokeAsync_CorrelationId_MatchesResponse()
    {
        // Two concurrent invocations — responses must be routed to the correct TCS
        var channel = new FakeDeviceChannel();
        var node = CreateNode(channel);
        node.StartReceiveLoop();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var cmd1 = new DeviceCommand("clipboard", "read", timeout: TimeSpan.FromSeconds(5));
        var cmd2 = new DeviceCommand("notification", "send", timeout: TimeSpan.FromSeconds(5));

        var task1 = node.InvokeAsync(cmd1, cts.Token);
        var task2 = node.InvokeAsync(cmd2, cts.Token);

        // Read both server-sent messages and capture correlation IDs
        var msg1 = await channel.ReadServerMessageAsync(cts.Token);
        var msg2 = await channel.ReadServerMessageAsync(cts.Token);

        string ExtractCorrelationId(string json)
        {
            using var d = JsonDocument.Parse(json);
            return d.RootElement.GetProperty("correlationId").GetString()!;
        }

        var cid1 = ExtractCorrelationId(msg1);
        var cid2 = ExtractCorrelationId(msg2);
        cid1.ShouldNotBe(cid2);

        // Reply in reverse order to verify correct routing
        await channel.SimulateDeviceSendAsync(JsonSerializer.Serialize(new
        {
            type = "result", correlationId = cid2, success = true, textResult = "response-2"
        }, JsonOpts));

        await channel.SimulateDeviceSendAsync(JsonSerializer.Serialize(new
        {
            type = "result", correlationId = cid1, success = true, textResult = "response-1"
        }, JsonOpts));

        var result1 = await task1;
        var result2 = await task2;

        result1.TextResult.ShouldBe("response-1");
        result2.TextResult.ShouldBe("response-2");
    }

    [Fact]
    public async Task InvokeAsync_FailureResponse_ReturnsFailResult()
    {
        var channel = new FakeDeviceChannel();
        var node = CreateNode(channel);
        node.StartReceiveLoop();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var command = new DeviceCommand("clipboard", "read", timeout: TimeSpan.FromSeconds(5));
        var invocationTask = node.InvokeAsync(command, cts.Token);

        var json = await channel.ReadServerMessageAsync(cts.Token);
        using var doc = JsonDocument.Parse(json);
        var correlationId = doc.RootElement.GetProperty("correlationId").GetString();

        await channel.SimulateDeviceSendAsync(JsonSerializer.Serialize(new
        {
            type = "result",
            correlationId,
            success = false,
            error = "Clipboard access denied"
        }, JsonOpts));

        var result = await invocationTask;
        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("Clipboard access denied");
    }

    [Fact]
    public async Task InvokeAsync_Timeout_ReturnsFailResultWithTimeoutMessage()
    {
        var channel = new FakeDeviceChannel();
        var node = CreateNode(channel);
        node.StartReceiveLoop();

        // Very short timeout so the test stays fast
        var command = new DeviceCommand("clipboard", "read", timeout: TimeSpan.FromMilliseconds(100));
        var result = await node.InvokeAsync(command);

        result.Success.ShouldBeFalse();
        result.Error.ShouldNotBeNull();
        result.Error!.ShouldContain("timed out");
    }

    [Fact]
    public async Task InvokeAsync_DeviceDisconnects_ReturnsFailResult()
    {
        var channel = new FakeDeviceChannel();
        var node = CreateNode(channel);
        node.StartReceiveLoop();

        var command = new DeviceCommand("clipboard", "read", timeout: TimeSpan.FromSeconds(5));
        var invocationTask = node.InvokeAsync(command);

        // Read the message so it's consumed
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await channel.ReadServerMessageAsync(cts.Token);

        // Simulate device disconnecting before responding
        channel.SimulateClose();

        var result = await invocationTask;
        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("disconnected");
    }

    [Fact]
    public async Task InvokeAsync_WhenNotConnected_ReturnsFailResult()
    {
        var channel = new FakeDeviceChannel();
        var node = CreateNode(channel);
        // Do NOT start the receive loop — state stays Pairing

        var command = new DeviceCommand("clipboard", "read");
        var result = await node.InvokeAsync(command);

        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("not connected");
    }

    [Fact]
    public async Task ReceiveLoop_Disconnect_SetsStateToDisconnected()
    {
        var channel = new FakeDeviceChannel();
        var node = CreateNode(channel);
        node.StartReceiveLoop();

        channel.SimulateClose();

        // Give the receive loop a moment to process the close
        await Task.Delay(100);

        node.State.ShouldBe(DeviceConnectionState.Disconnected);
    }

    [Fact]
    public async Task ReceiveLoop_Disconnect_RaisesDisconnectedEvent()
    {
        var channel = new FakeDeviceChannel();
        var node = CreateNode(channel);
        var disconnectedRaised = false;
        node.Disconnected += () => disconnectedRaised = true;

        node.StartReceiveLoop();
        channel.SimulateClose();

        await Task.Delay(100);

        disconnectedRaised.ShouldBeTrue();
    }

    [Fact]
    public async Task InvokeAsync_BinaryResult_DecodesBase64()
    {
        var channel = new FakeDeviceChannel();
        var node = CreateNode(channel);
        node.StartReceiveLoop();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var command = new DeviceCommand("screenshot", "capture", timeout: TimeSpan.FromSeconds(5));
        var invocationTask = node.InvokeAsync(command, cts.Token);

        var json = await channel.ReadServerMessageAsync(cts.Token);
        using var doc = JsonDocument.Parse(json);
        var correlationId = doc.RootElement.GetProperty("correlationId").GetString();

        var fakeBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic
        await channel.SimulateDeviceSendAsync(JsonSerializer.Serialize(new
        {
            type = "result",
            correlationId,
            success = true,
            binaryResultBase64 = Convert.ToBase64String(fakeBytes),
            mimeType = "image/png"
        }, JsonOpts));

        var result = await invocationTask;
        result.Success.ShouldBeTrue();
        result.BinaryResult.ShouldNotBeNull();
        result.BinaryResult!.Length.ShouldBe(4);
        result.MimeType.ShouldBe("image/png");
    }
}
