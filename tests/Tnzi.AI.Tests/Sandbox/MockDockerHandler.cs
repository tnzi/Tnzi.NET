using System.Net;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// Mock HTTP handler that simulates Docker Engine API responses for testing
/// </summary>
internal sealed class MockDockerHandler : HttpMessageHandler
{
    private readonly List<MockRoute> _routes = [];
    private TimeSpan _delay = TimeSpan.Zero;
    public List<RequestRecord> RequestLog { get; } = [];

    private string _execStdout = "";
    private string _execStderr = "";
    private int _execExitCode;
    private bool _execFlowConfigured;

    public void SetupResponse(string pathContains, HttpStatusCode statusCode, string body,
        HttpMethod? method = null)
    {
        _routes.Add(new MockRoute(pathContains, statusCode, body, method));
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
                var content = BuildMultiplexedOutput(_execStdout, _execStderr);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content)
                };
            }

            if (url.Contains("/exec") && url.Contains("/json"))
            {
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

    public record RequestRecord(HttpMethod Method, string Url, string? Body);
    private record MockRoute(string PathContains, HttpStatusCode StatusCode, string Body, HttpMethod? Method);
}
