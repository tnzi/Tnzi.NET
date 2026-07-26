using System.IO.Pipes;
using System.Net.Http;
using System.Net.Sockets;

namespace Tnzi.AI.Sandbox.Providers.Docker;

/// <summary>
/// Provides <see cref="SocketsHttpHandler.ConnectCallback"/> implementations for the
/// three Docker host transports: Unix domain socket (Linux/macOS), Windows named pipe,
/// and TCP. The .NET runtime then handles all HTTP/1.1 framing (chunked encoding,
/// content-length, trailers) so we no longer hand-parse responses.
/// </summary>
internal static class DockerSocketConnector
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Builds a <see cref="SocketsHttpHandler.ConnectCallback"/> that resolves the
    /// supplied Docker host scheme. Supported: <c>unix://</c>, <c>npipe://</c>, <c>tcp://</c>.
    /// </summary>
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> CreateConnectCallback(string dockerHost)
    {
        Check.NotNullOrWhiteSpace(dockerHost);

        if (dockerHost.StartsWith("unix://", StringComparison.OrdinalIgnoreCase))
        {
            var socketPath = dockerHost["unix://".Length..];
            return (_, ct) => ConnectUnixAsync(socketPath, ct);
        }

        if (dockerHost.StartsWith("npipe://", StringComparison.OrdinalIgnoreCase))
        {
            // Windows named pipe - strip scheme; NamedPipeClientStream needs server name + pipe name separately.
            // Docker Desktop default: npipe:////./pipe/docker_engine → server="." pipe="docker_engine"
            var raw = dockerHost["npipe://".Length..];
            var (serverName, pipeName) = ParseNpipePath(raw);
            return (_, ct) => ConnectNamedPipeAsync(serverName, pipeName, ct);
        }

        if (dockerHost.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(dockerHost);
            return (_, ct) => ConnectTcpAsync(uri.Host, uri.Port, ct);
        }

        throw new InvalidOperationException($"Unsupported Docker host scheme: {dockerHost}");
    }

    private static async ValueTask<Stream> ConnectUnixAsync(string socketPath, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ConnectTimeout);

        try
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), timeoutCts.Token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Docker unix socket connection timed out after {ConnectTimeout.TotalSeconds}s: {socketPath}");
        }
    }

    private static async ValueTask<Stream> ConnectNamedPipeAsync(string serverName, string pipeName, CancellationToken ct)
    {
        // NamedPipeClientStream is the correct API for Windows pipes; UnixDomainSocketEndPoint
        // does not understand the npipe transport on Windows.
        var pipe = new NamedPipeClientStream(serverName, pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync((int)ConnectTimeout.TotalMilliseconds, ct);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync();
            throw;
        }
    }

    private static async ValueTask<Stream> ConnectTcpAsync(string host, int port, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ConnectTimeout);

        try
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(host, port, timeoutCts.Token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Docker TCP connection timed out after {ConnectTimeout.TotalSeconds}s: {host}:{port}");
        }
    }

    /// <summary>
    /// Parses a Docker npipe URL into (serverName, pipeName).
    /// Examples:
    ///   "//./pipe/docker_engine"  → (".",  "docker_engine")
    ///   "//host/pipe/docker_engine" → ("host", "docker_engine")
    /// </summary>
    private static (string ServerName, string PipeName) ParseNpipePath(string raw)
    {
        // Normalize separators
        raw = raw.Replace('\\', '/').TrimStart('/');
        // Now: "./pipe/docker_engine" or "host/pipe/docker_engine"
        var firstSlash = raw.IndexOf('/');
        if (firstSlash < 0)
            throw new InvalidOperationException($"Invalid Docker npipe path: {raw}");

        var serverName = raw[..firstSlash];
        var rest = raw[(firstSlash + 1)..];

        // rest typically starts with "pipe/"
        const string pipePrefix = "pipe/";
        if (rest.StartsWith(pipePrefix, StringComparison.OrdinalIgnoreCase))
            rest = rest[pipePrefix.Length..];

        return (serverName, rest);
    }
}
