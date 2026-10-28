using System.Net;
using System.Net.Sockets;
using System.Text;
using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests.Infrastructure.Mcp;

/// <summary>
/// MCP 客户端的 SSRF 防线必须在<b>建立连接时</b>生效，而不只是在注册表增改时检查一次。
/// </summary>
/// <remarks>
/// 注册表端点由租户管理员填写，注册时 EgressGuard 只检查了那一刻的解析结果。把 DNS 记录事后改指内网
/// （或注册时就是一个先答公网、再答内网的域名），下一次连接 / 重连就带着初始化请求体连进内网；
/// 服务器回一个 307 指向内网地址同理。这里用本机回环当「内网」：未开 AllowPrivateEndpoints 时，
/// 注册表来源的端点一次 TCP 连接都不应该建立。
/// </remarks>
public class McpEgressAtConnectTimeTests
{
    [Fact]
    public async Task RegistryEndpoint_ResolvingToAnInternalAddress_IsRefusedBeforeConnecting()
    {
        using var server = new LoopbackHttpServer(_ => NotFound());
        var factory = CreateFactory(allowPrivateEndpoints: false);
        var config = HttpConfig($"http://localhost:{server.Port}/mcp", enforceEgressGuard: true);

        await Should.ThrowAsync<Exception>(() => factory.CreateAndConnectInternalAsync(config, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)));

        server.Requests.ShouldBeEmpty();
        server.ConnectionCount.ShouldBe(0);
    }

    [Fact]
    public async Task RegistryEndpoint_WithAllowPrivateEndpoints_IsAllowedToConnect()
    {
        using var server = new LoopbackHttpServer(_ => NotFound());
        var factory = CreateFactory(allowPrivateEndpoints: true);
        var config = HttpConfig($"http://127.0.0.1:{server.Port}/mcp", enforceEgressGuard: true);

        await Should.ThrowAsync<Exception>(() => factory.CreateAndConnectInternalAsync(config, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)));

        server.Requests.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task DeploymentEndpoint_IsHostTrustedAndMayBeInternal()
    {
        using var server = new LoopbackHttpServer(_ => NotFound());
        var factory = CreateFactory(allowPrivateEndpoints: false);
        var config = HttpConfig($"http://127.0.0.1:{server.Port}/mcp", enforceEgressGuard: false);

        await Should.ThrowAsync<Exception>(() => factory.CreateAndConnectInternalAsync(config, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)));

        server.Requests.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Redirect_IsNotFollowed()
    {
        // 307 保留方法与请求体：跟随它就是把初始化 POST（连同凭据头）原样投到 Location 指的地方
        using var target = new LoopbackHttpServer(_ => NotFound());
        using var server = new LoopbackHttpServer(_ => Redirect($"http://127.0.0.1:{target.Port}/internal"));
        var factory = CreateFactory(allowPrivateEndpoints: false);
        var config = HttpConfig($"http://127.0.0.1:{server.Port}/mcp", enforceEgressGuard: false);

        await Should.ThrowAsync<Exception>(() => factory.CreateAndConnectInternalAsync(config, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)));

        server.Requests.ShouldNotBeEmpty();
        target.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ConfiguredHeaders_AreSentAsHeaders_NeverInTheQueryString()
    {
        // 查询串里的凭据会进访问日志、代理日志与错误信息；凭据只能走请求头
        using var server = new LoopbackHttpServer(_ => NotFound());
        var factory = CreateFactory(allowPrivateEndpoints: false);
        var config = HttpConfig($"http://127.0.0.1:{server.Port}/mcp", enforceEgressGuard: false);
        config.Headers["X-Api-Key"] = "secret-key";

        await Should.ThrowAsync<Exception>(() => factory.CreateAndConnectInternalAsync(config, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)));

        server.Requests.ShouldNotBeEmpty();
        server.Requests.ShouldAllBe(r => !r.RequestLine.Contains("secret-key"));
        server.Requests.ShouldContain(r => r.Headers.Contains("X-Api-Key: secret-key"));
    }

    /// <summary>
    /// 受守卫的处理器不走代理：经代理时套接字连的是代理，目标主机由代理解析，连接回调检查的只是代理的地址，
    /// 注册表端点改指内网照样由代理连进去。上面几条在没有配代理的测试机上看不出这一点（回环地址也从不经代理），
    /// 所以这一条直接在处理器上断言。
    /// </summary>
    [Fact]
    public void RegistryEndpoint_Handler_BypassesTheSystemProxy()
    {
        using var guarded = McpClientFactory.CreateHttpHandler(HttpConfig("https://mcp.example.com/mcp", enforceEgressGuard: true), allowPrivateEndpoints: false);

        guarded.UseProxy.ShouldBeFalse();
        guarded.ConnectCallback.ShouldNotBeNull();
        guarded.AllowAutoRedirect.ShouldBeFalse();
    }

    /// <summary>对照：宿主信任的端点（部署配置、或开了私网放行）照常跟随系统代理，但仍不跟随重定向。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void TrustedEndpoint_Handler_KeepsTheSystemProxy(bool enforceEgressGuard, bool allowPrivateEndpoints)
    {
        using var handler = McpClientFactory.CreateHttpHandler(HttpConfig("https://mcp.example.com/mcp", enforceEgressGuard), allowPrivateEndpoints);

        handler.UseProxy.ShouldBeTrue();
        handler.ConnectCallback.ShouldBeNull();
        handler.AllowAutoRedirect.ShouldBeFalse();
    }

    [Fact]
    public void RegistryRegistration_IsMappedWithTheConnectTimeGuard()
    {
        var config = McpServerRegistrationMapper.ToServerConfig(
            new McpServerRegistration { Name = "remote", ServerUrl = "https://mcp.example.com/mcp", Transport = "streamable-http" },
            token => token);

        config.EnforceEgressGuard.ShouldBeTrue();
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static McpClientFactory CreateFactory(bool allowPrivateEndpoints)
    {
        var options = new AIOptions();
        options.Mcp.AllowPrivateEndpoints = allowPrivateEndpoints;
        return new McpClientFactory(
            NullLoggerFactory.Instance,
            NullLogger<McpClientFactory>.Instance,
            oauthHandler: null,
            aiOptions: new StaticOptionsMonitor<AIOptions>(options));
    }

    private static McpServerConfig HttpConfig(string endpoint, bool enforceEgressGuard) => new()
    {
        Name = "test-server",
        Endpoint = endpoint,
        ConnectionType = McpConnectionType.Http,
        EnforceEgressGuard = enforceEgressGuard
    };

    private static string NotFound()
        => "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";

    private static string Redirect(string location)
        => $"HTTP/1.1 307 Temporary Redirect\r\nLocation: {location}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";

    private sealed record RecordedRequest(string RequestLine, string Headers);

    /// <summary>回环上的最小 HTTP 服务器：记录每个请求的请求行与头，按给定的原始响应作答。</summary>
    private sealed class LoopbackHttpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentQueue<RecordedRequest> _requests = new();
        private int _connections;

        public LoopbackHttpServer(Func<RecordedRequest, string> respond)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptLoopAsync(respond);
        }

        public int Port { get; }
        public int ConnectionCount => Volatile.Read(ref _connections);
        public IReadOnlyList<RecordedRequest> Requests => [.. _requests];

        private async Task AcceptLoopAsync(Func<RecordedRequest, string> respond)
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch
                {
                    return;
                }

                Interlocked.Increment(ref _connections);
                _ = HandleAsync(client, respond);
            }
        }

        private async Task HandleAsync(TcpClient client, Func<RecordedRequest, string> respond)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var buffer = new byte[16 * 1024];
                    var received = new StringBuilder();
                    while (!received.ToString().Contains("\r\n\r\n"))
                    {
                        var read = await stream.ReadAsync(buffer, _cts.Token);
                        if (read == 0) return;
                        received.Append(Encoding.ASCII.GetString(buffer, 0, read));
                    }

                    var head = received.ToString();
                    var headerEnd = head.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    var lines = head[..headerEnd].Split("\r\n");
                    var request = new RecordedRequest(lines[0], string.Join("\n", lines.Skip(1)));
                    _requests.Enqueue(request);

                    var response = Encoding.ASCII.GetBytes(respond(request));
                    await stream.WriteAsync(response, _cts.Token);
                    await stream.FlushAsync(_cts.Token);
                }
                catch
                {
                    // 客户端中途断开：测试只关心请求是否到达
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }
}
