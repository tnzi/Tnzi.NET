namespace Tnzi.AI.Device.Transport;

/// <summary>
/// Handles the WebSocket handshake for remote devices connecting to <c>/ws/device</c>.
/// <para>Protocol:</para>
/// <list type="number">
///   <item>HTTP upgrade → WebSocket.</item>
///   <item>Device sends <c>register</c> message with pairing code and capabilities.</item>
///   <item>Server validates code via <see cref="IDevicePairingService"/>, constructs
///         <see cref="WebSocketDeviceNode"/>, registers it in <see cref="IDeviceRegistry"/>,
///         and replies with <c>registered</c>.</item>
///   <item>Server reads the WS until the device disconnects; then unregisters the node.</item>
/// </list>
/// </summary>
public sealed class WebSocketDeviceConnectionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IDevicePairingService _pairingService;
    private readonly IDeviceRegistry _registry;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<WebSocketDeviceConnectionHandler> _logger;

    public WebSocketDeviceConnectionHandler(
        IDevicePairingService pairingService,
        IDeviceRegistry registry,
        ILoggerFactory loggerFactory,
        ILogger<WebSocketDeviceConnectionHandler> logger)
    {
        _pairingService = Check.NotNull(pairingService);
        _registry = Check.NotNull(registry);
        _loggerFactory = Check.NotNull(loggerFactory);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// Entry point called from the <c>/ws/device</c> route.
    /// Validates that the request is a WebSocket upgrade; returns 400 otherwise.
    /// </summary>
    public async Task HandleAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("WebSocket connection required.", cancellationToken);
            return;
        }

        var ws = await context.WebSockets.AcceptWebSocketAsync();
        var channel = new WebSocketDeviceChannel(ws);

        await RunHandshakeAndReceiveLoopAsync(channel, cancellationToken);
    }

    /// <summary>
    /// Run the full handshake and receive loop over <paramref name="channel"/>.
    /// <para>This overload is <c>public</c> to facilitate unit testing with a fake/in-memory channel.
    /// Production code calls <see cref="HandleAsync"/> instead.</para>
    /// </summary>
    public async Task RunHandshakeAndReceiveLoopAsync(IDeviceChannel channel, CancellationToken cancellationToken)
    {
        // ── Step 1: wait for the register message ────────────────────────────
        WsRegisterMessage? registerMsg;
        try
        {
            var raw = await channel.ReceiveAsync(cancellationToken);
            if (raw is null)
            {
                _logger.LogWarning("Device channel closed before sending register message.");
                return;
            }

            registerMsg = JsonSerializer.Deserialize<WsRegisterMessage>(raw, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to receive/parse register message from device.");
            await RejectAsync(channel, "Invalid register message.", cancellationToken);
            return;
        }

        if (registerMsg is null || registerMsg.Type != "register" || string.IsNullOrWhiteSpace(registerMsg.PairingCode))
        {
            _logger.LogWarning("Device sent invalid register message (missing type or pairing code).");
            await RejectAsync(channel, "Invalid register message: 'type' must be 'register' and 'pairingCode' is required.", cancellationToken);
            return;
        }

        // ── Step 2: validate pairing code ───────────────────────────────────
        var pairingInfo = await _pairingService.ApprovePairingAsync(registerMsg.PairingCode, cancellationToken);
        if (pairingInfo is null)
        {
            _logger.LogWarning("Device supplied invalid or expired pairing code.");
            await RejectAsync(channel, "Pairing code is invalid or has expired.", cancellationToken);
            return;
        }

        // ── Step 3: parse capabilities ───────────────────────────────────────
        var capabilities = ParseCapabilities(registerMsg.Capabilities);
        var platform = ParsePlatform(registerMsg.Platform);
        var nodeId = pairingInfo.NodeId;
        var name = string.IsNullOrWhiteSpace(registerMsg.Name) ? pairingInfo.Name : registerMsg.Name;

        // ── Step 4: construct the node and register it ───────────────────────
        var nodeLogger = _loggerFactory.CreateLogger<WebSocketDeviceNode>();
        var node = new WebSocketDeviceNode(nodeId, name, platform, capabilities, channel, nodeLogger);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        node.Disconnected += () =>
        {
            _registry.Unregister(nodeId);
            closed.TrySetResult();
        };
        node.StartReceiveLoop(cancellationToken);

        _registry.Register(node);

        // ── Step 5: send registered ack ────────────────────────────────────
        var ack = new WsRegisteredMessage { NodeId = nodeId };
        var ackJson = JsonSerializer.Serialize(ack, JsonOptions);
        try
        {
            await channel.SendAsync(ackJson, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send registered ack to device {NodeId}", nodeId);
        }

        _logger.LogInformation("WebSocket device registered: {NodeId} ({Name}, {Platform}, {Count} capabilities)",
            nodeId, name, platform, capabilities.Count);

        // ── Step 6: wait for the channel to close (receive loop drives this) ─
        // Wait on the Disconnected event (no polling); also unblock on cancellation so the
        // middleware pipeline doesn't hold the HttpContext open after shutdown. Registering a
        // callback that completes the TCS avoids the OperationCanceledException a cancelled
        // Task.Delay would throw into the request pipeline.
        await using (cancellationToken.Register(static s => ((TaskCompletionSource)s!).TrySetResult(), closed))
        {
            await closed.Task.ConfigureAwait(false);
        }

        await node.DisposeAsync();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static async Task RejectAsync(IDeviceChannel channel, string reason, CancellationToken cancellationToken)
    {
        try
        {
            var msg = new WsRejectedMessage { Reason = reason };
            var json = JsonSerializer.Serialize(msg, JsonOptions);
            await channel.SendAsync(json, cancellationToken);
        }
        catch (Exception) { /* Channel may already be closed */ }
    }

    private static IReadOnlyList<DeviceCapability> ParseCapabilities(List<WsCapabilityDto> dtos)
    {
        var result = new List<DeviceCapability>(dtos.Count);
        foreach (var dto in dtos)
        {
            if (string.IsNullOrWhiteSpace(dto.Family)) continue;

            var level = dto.Permission?.ToLowerInvariant() switch
            {
                "elevated" => DevicePermissionLevel.Elevated,
                _ => DevicePermissionLevel.Basic
            };

            result.Add(new DeviceCapability(dto.Family, dto.Commands ?? [], level));
        }

        return result.AsReadOnly();
    }

    private static DevicePlatform ParsePlatform(string? platform) =>
        platform?.ToLowerInvariant() switch
        {
            "windows" => DevicePlatform.Windows,
            "macos" or "mac" => DevicePlatform.MacOS,
            "linux" => DevicePlatform.Linux,
            "ios" => DevicePlatform.iOS,
            "android" => DevicePlatform.Android,
            _ => DevicePlatform.Unknown
        };
}
