namespace Tnzi.AI.Device.Transport;

/// <summary>
/// Bidirectional message channel between the server and a remote device.
/// Abstracted so that unit tests can inject an in-memory fake without a real WebSocket.
/// </summary>
public interface IDeviceChannel
{
    /// <summary>
    /// Send a raw JSON message to the remote device.
    /// </summary>
    Task SendAsync(string json, CancellationToken cancellationToken = default);

    /// <summary>
    /// Receive one raw JSON message from the remote device.
    /// Returns <c>null</c> when the channel is closed.
    /// </summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the channel is currently open.
    /// </summary>
    bool IsOpen { get; }
}
