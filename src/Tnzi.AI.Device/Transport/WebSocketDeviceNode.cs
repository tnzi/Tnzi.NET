namespace Tnzi.AI.Device.Transport;

/// <summary>
/// Remote device node connected over a WebSocket (or any <see cref="IDeviceChannel"/>).
/// <para>
/// Design:
/// <list type="bullet">
///   <item>Capabilities and platform are learned from the device's initial <c>register</c> message.</item>
///   <item>Each <see cref="InvokeAsync"/> serialises a <c>WsInvokeMessage</c> with a unique
///         <c>CorrelationId</c>, parks a <see cref="TaskCompletionSource{T}"/> in a concurrent dict,
///         and awaits the matching <c>WsResultMessage</c> from the receive loop.</item>
///   <item>A dedicated background loop (<see cref="StartReceiveLoopAsync"/>) dispatches inbound
///         messages to the waiting TCSs and raises <see cref="Disconnected"/> when the channel closes.</item>
/// </list>
/// </para>
/// </summary>
public sealed class WebSocketDeviceNode : IDeviceNode, IAsyncDisposable
{
    private readonly IDeviceChannel _channel;
    private readonly ILogger<WebSocketDeviceNode> _logger;

    // Outstanding invocations keyed by CorrelationId
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DeviceCommandResult>> _pending =
        new(StringComparer.Ordinal);

    private volatile DeviceConnectionState _state = DeviceConnectionState.Pairing;
    private CancellationTokenSource? _receiveCts;

    /// <summary>Raised when the channel disconnects (from the receive loop).</summary>
    public event Action? Disconnected;

    public string NodeId { get; }
    public string Name { get; }
    public DevicePlatform Platform { get; }
    public IReadOnlyList<DeviceCapability> Capabilities { get; }
    public DeviceConnectionState State => _state;

    public WebSocketDeviceNode(
        string nodeId,
        string name,
        DevicePlatform platform,
        IReadOnlyList<DeviceCapability> capabilities,
        IDeviceChannel channel,
        ILogger<WebSocketDeviceNode> logger)
    {
        NodeId = Check.NotNullOrWhiteSpace(nodeId);
        Name = Check.NotNullOrWhiteSpace(name);
        Platform = platform;
        Capabilities = Check.NotNull(capabilities);
        _channel = Check.NotNull(channel);
        _logger = Check.NotNull(logger);
    }

    // ─── Receive loop ─────────────────────────────────────────────────────────

    /// <summary>
    /// Start the background receive loop.  Call once after construction.
    /// </summary>
    public void StartReceiveLoop(CancellationToken applicationStopping = default)
    {
        _state = DeviceConnectionState.Connected;
        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(applicationStopping);
        _ = ReceiveLoopAsync(_receiveCts.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _channel.IsOpen)
            {
                var raw = await _channel.ReceiveAsync(cancellationToken);
                if (raw is null)
                {
                    break; // Channel closed
                }

                DispatchInbound(raw);
            }
        }
        catch (OperationCanceledException) { /* Expected on shutdown */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Receive loop error on device node {NodeId}", NodeId);
        }
        finally
        {
            HandleDisconnect();
        }
    }

    private void DispatchInbound(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var type = root.TryGetProperty("type", out var typeProp)
                ? typeProp.GetString()
                : null;

            if (type == "result")
            {
                var correlationId = root.TryGetProperty("correlationId", out var cidProp)
                    ? cidProp.GetString() ?? string.Empty
                    : string.Empty;

                if (_pending.TryRemove(correlationId, out var tcs))
                {
                    tcs.TrySetResult(ParseResult(root));
                }
                else
                {
                    _logger.LogWarning("Device {NodeId}: received result for unknown correlationId={CorrelationId}",
                        NodeId, correlationId);
                }
            }
            else
            {
                _logger.LogDebug("Device {NodeId}: ignored inbound message type={Type}", NodeId, type);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Device {NodeId}: malformed inbound message", NodeId);
        }
    }

    private static DeviceCommandResult ParseResult(JsonElement root)
    {
        var success = root.TryGetProperty("success", out var sp) && sp.GetBoolean();

        if (!success)
        {
            var error = root.TryGetProperty("error", out var ep) ? ep.GetString() : null;
            return DeviceCommandResult.Fail(error ?? "Remote device returned an unspecified error.");
        }

        var text = root.TryGetProperty("textResult", out var tp) ? tp.GetString() : null;

        byte[]? binary = null;
        if (root.TryGetProperty("binaryResultBase64", out var bp) && bp.ValueKind == JsonValueKind.String)
        {
            var b64 = bp.GetString();
            if (!string.IsNullOrEmpty(b64))
            {
                binary = Convert.FromBase64String(b64);
            }
        }

        var mimeType = root.TryGetProperty("mimeType", out var mp) ? mp.GetString() : null;
        return DeviceCommandResult.Ok(text, binary, mimeType);
    }

    private void HandleDisconnect()
    {
        _state = DeviceConnectionState.Disconnected;

        // Cancel all outstanding invocations
        foreach (var tcs in _pending.Values)
        {
            tcs.TrySetResult(DeviceCommandResult.Fail("Device disconnected before the command completed."));
        }
        _pending.Clear();

        _logger.LogInformation("Device node disconnected: {NodeId}", NodeId);
        Disconnected?.Invoke();
    }

    // ─── IDeviceNode.InvokeAsync ──────────────────────────────────────────────

    public async Task<DeviceCommandResult> InvokeAsync(DeviceCommand command, CancellationToken cancellationToken = default)
    {
        Check.NotNull(command);

        if (_state != DeviceConnectionState.Connected || !_channel.IsOpen)
        {
            return DeviceCommandResult.Fail($"Device '{NodeId}' is not connected (state={_state}).");
        }

        var correlationId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<DeviceCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = tcs;

        // Build the invoke message
        var invoke = new WsInvokeMessage
        {
            CorrelationId = correlationId,
            Family = command.Family,
            Command = command.Command,
            Parameters = command.Parameters,
            TimeoutSeconds = (int)command.Timeout.TotalSeconds
        };

        try
        {
            var json = JsonSerializer.Serialize(invoke, JsonOptions);
            await _channel.SendAsync(json, cancellationToken);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(correlationId, out _);
            return DeviceCommandResult.Fail($"Failed to send command to device '{NodeId}': {ex.Message}");
        }

        // Await the response with the command's timeout + a small scheduling grace
        using var timeoutCts = new CancellationTokenSource(command.Timeout + TimeSpan.FromSeconds(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        // Dispose the registration when the await completes so the timeout callback
        // (which captures tcs) is released promptly rather than living until the linked CTS dies.
        using var registration = linked.Token.Register(() =>
        {
            if (_pending.TryRemove(correlationId, out var t))
            {
                t.TrySetResult(DeviceCommandResult.Fail(
                    $"Command '{command.Family}/{command.Command}' on device '{NodeId}' timed out after {(int)command.Timeout.TotalSeconds}s."));
            }
        });

        try
        {
            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(correlationId, out _);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async ValueTask DisposeAsync()
    {
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();

        if (_channel is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (_channel is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
