using System.Net;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// Mock HTTP handler that simulates Docker Engine API responses for testing
/// </summary>
internal sealed class MockDockerHandler : HttpMessageHandler
{
    private readonly List<MockRoute> _routes = [];
    private readonly List<MockSequence> _sequences = [];
    private TimeSpan _delay = TimeSpan.Zero;
    public List<RequestRecord> RequestLog { get; } = [];

    private string _execStdout = "";
    private string _execStderr = "";
    private int _execExitCode;
    private bool _execFlowConfigured;
    private bool _execHangsAfterOutput;
    private IReadOnlyList<(byte Stream, byte[] Payload)>? _execFrames;
    private (HttpStatusCode Status, string Body)? _execInspect;

    public void SetupResponse(string pathContains, HttpStatusCode statusCode, string body,
        HttpMethod? method = null)
    {
        _routes.Add(new MockRoute(pathContains, statusCode, body, method));
    }

    /// <summary>
    /// Answer successive matching requests with these responses in order; the last one repeats once
    /// the sequence is used up. Takes precedence over <see cref="SetupResponse"/> routes.
    /// </summary>
    public void SetupResponseSequence(string pathContains, HttpMethod method,
        params (HttpStatusCode Status, string Body)[] responses)
    {
        _sequences.Add(new MockSequence(pathContains, method, new Queue<(HttpStatusCode, string)>(responses)));
    }

    public void SetupDelay(TimeSpan delay)
    {
        _delay = delay;
    }

    /// <summary>
    /// Configure the mock to handle exec create → start → inspect flow
    /// </summary>
    public void SetupExecFlow(int exitCode, string stdout, string? stderr = null)
    {
        _execFlowConfigured = true;
        _execExitCode = exitCode;
        _execStdout = stdout;
        _execStderr = stderr ?? "";
    }

    /// <summary>
    /// Configure the exec flow to emit the given output and then never end the stream,
    /// simulating a command that prints diagnostics and then hangs until the client
    /// gives up. Used to prove the timeout path keeps output collected so far.
    /// </summary>
    public void SetupExecFlowThenHang(string stdout, string? stderr = null)
    {
        SetupExecFlow(exitCode: 0, stdout: stdout, stderr: stderr);
        _execHangsAfterOutput = true;
    }

    /// <summary>
    /// Configure the exec flow to replay these exact multiplexed frames. Docker cuts its
    /// frames at arbitrary byte positions (one frame per read of the exec pipe), so a test
    /// that needs a multi-byte character straddling two frames has to place the cut itself.
    /// </summary>
    public void SetupExecFlowWithFrames(int exitCode, params (byte Stream, byte[] Payload)[] frames)
    {
        SetupExecFlow(exitCode, stdout: "");
        _execFrames = frames;
    }

    /// <summary>
    /// Override the exec inspect (<c>/exec/{id}/json</c>) response, e.g. a daemon error or a
    /// body whose <c>ExitCode</c> is null.
    /// </summary>
    public void SetupExecInspect(HttpStatusCode status, string body)
    {
        _execInspect = (status, body);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_delay > TimeSpan.Zero)
            await Task.Delay(_delay, cancellationToken);

        var url = request.RequestUri?.PathAndQuery ?? "";
        var requestBody = request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken)
            : null;

        RequestLog.Add(new RequestRecord(request.Method, url, requestBody));

        // Handle exec flow
        if (_execFlowConfigured)
        {
            if (url.Contains("/exec") && url.Contains("/start"))
            {
                var content = _execFrames is null
                    ? BuildMultiplexedOutput(_execStdout, _execStderr)
                    : BuildFrames(_execFrames);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = _execHangsAfterOutput
                        ? new StreamContent(new HangingStream(content))
                        : new ByteArrayContent(content)
                };
            }

            if (url.Contains("/exec") && url.Contains("/json"))
            {
                if (_execInspect is { } inspect)
                {
                    return new HttpResponseMessage(inspect.Status)
                    {
                        Content = new StringContent(inspect.Body)
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$$"""{"ExitCode":{{{_execExitCode}}}}""")
                };
            }

            if (url.Contains("/exec") && request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("""{"Id":"exec-test-001"}""")
                };
            }
        }

        var sequence = _sequences.FirstOrDefault(s =>
            url.Contains(s.PathContains, StringComparison.OrdinalIgnoreCase) && s.Method == request.Method);
        if (sequence is not null)
        {
            var (status, body) = sequence.Responses.Count > 1 ? sequence.Responses.Dequeue() : sequence.Responses.Peek();
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }

        // Check explicit routes
        var route = _routes.FirstOrDefault(r =>
            url.Contains(r.PathContains, StringComparison.OrdinalIgnoreCase)
            && (r.Method is null || r.Method == request.Method));

        if (route is not null)
        {
            return new HttpResponseMessage(route.StatusCode)
            {
                Content = new StringContent(route.Body)
            };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"No mock route for {request.Method} {url}")
        };
    }

    /// <summary>
    /// Build Docker multiplexed stream format:
    /// [stream_type(1 byte)][0,0,0][size(4 bytes big-endian)][payload]
    /// </summary>
    private static byte[] BuildMultiplexedOutput(string stdout, string stderr)
    {
        using var ms = new MemoryStream();

        if (!string.IsNullOrEmpty(stdout))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(stdout);
            ms.WriteByte(1); // stdout
            ms.Write(new byte[3]); // padding
            ms.Write(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(bytes.Length)));
            ms.Write(bytes);
        }

        if (!string.IsNullOrEmpty(stderr))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(stderr);
            ms.WriteByte(2); // stderr
            ms.Write(new byte[3]); // padding
            ms.Write(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(bytes.Length)));
            ms.Write(bytes);
        }

        return ms.ToArray();
    }

    private static byte[] BuildFrames(IEnumerable<(byte Stream, byte[] Payload)> frames)
    {
        using var ms = new MemoryStream();
        foreach (var (stream, payload) in frames)
        {
            ms.WriteByte(stream);
            ms.Write(new byte[3]); // padding
            ms.Write(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(payload.Length)));
            ms.Write(payload);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Replays a fixed prefix and then blocks forever, modelling a Docker exec stream
    /// whose command produced output and then stopped making progress.
    /// </summary>
    private sealed class HangingStream(byte[] prefix) : Stream
    {
        private int _position;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < prefix.Length)
            {
                var count = Math.Min(buffer.Length, prefix.Length - _position);
                prefix.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public record RequestRecord(HttpMethod Method, string Url, string? Body);
    private record MockRoute(string PathContains, HttpStatusCode StatusCode, string Body, HttpMethod? Method);
    private record MockSequence(string PathContains, HttpMethod Method, Queue<(HttpStatusCode Status, string Body)> Responses);

    /// <summary>
    /// Every exec-create request's <c>Cmd</c> argv, in the order Docker received them.
    /// </summary>
    public List<string[]> ExecCommands()
    {
        var commands = new List<string[]>();
        foreach (var request in RequestLog.Where(r =>
                     r.Method == HttpMethod.Post && r.Url.Contains("/exec")
                     && !r.Url.Contains("/start") && !r.Url.Contains("/json")))
        {
            using var document = System.Text.Json.JsonDocument.Parse(request.Body!);
            // Docker's Go decoder matches property names case-insensitively, so the test must too.
            var cmd = document.RootElement.EnumerateObject()
                .First(p => string.Equals(p.Name, "Cmd", StringComparison.OrdinalIgnoreCase));
            commands.Add(cmd.Value.EnumerateArray().Select(e => e.GetString()!).ToArray());
        }

        return commands;
    }

    /// <summary>
    /// The shell command each exec ran: the last argv entry. With an in-container timeout the
    /// command travels as its own argument after the wrapper script; without one it is the
    /// <c>sh -c</c> script itself.
    /// </summary>
    public List<string> ExecShellCommands() => ExecCommands().Select(argv => argv[^1]).ToList();
}
