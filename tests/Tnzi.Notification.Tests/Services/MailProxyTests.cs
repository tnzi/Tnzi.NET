using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using MailKit.Net.Proxy;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// 邮件连接该不该经代理、经哪个代理（<see cref="MailProxySelector"/>），以及 <see cref="MailKitEmailSender"/> 真的经它发信。
/// </summary>
/// <remarks>
/// ★★ <b>补的是哪个洞。</b>没有 DNS、只能经 <c>HTTPS_PROXY</c> 出网的容器里，<c>HttpClient</c> 会读代理变量而 MailKit 不会，
/// 每一封信都死在解析 SMTP 主机名上。这里守三件事：代理从哪里来（显式配置 / 环境变量 / 关掉）、
/// 哪些目标绕过（<c>NO_PROXY</c>、回环）、以及经代理时主机名交给代理而不是本机解析。
/// </remarks>
public class MailProxyTests
{
    private const string SmtpHost = "smtp.example.com";

    private static Func<string, string?> Env(params (string Name, string Value)[] variables)
    {
        var map = variables.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return name => map.TryGetValue(name, out var value) ? value : null;
    }

    private static Uri? Select(Func<string, string?> env, string host = SmtpHost, int port = 587, string? configured = null, bool useEnvironment = true)
        => MailProxySelector.Select(configured, useEnvironment, host, port, env);

    // ── 代理从哪里来 ───────────────────────────────────────────────────────────

    /// <summary>★ 没有任何代理变量：直连，与此前一字不差。</summary>
    [Fact]
    public void NoProxyAnywhere_ConnectsDirectly()
        => Select(Env()).ShouldBeNull();

    /// <summary>★★★ 只给了 <c>HTTPS_PROXY</c>：经它。</summary>
    [Fact]
    public void HttpsProxyVariable_IsUsed()
        => Select(Env(("HTTPS_PROXY", "http://proxy.internal:3128")))!.ToString().ShouldBe("http://proxy.internal:3128/");

    /// <summary>小写优先，与 <c>HttpClient</c> 一致。</summary>
    [Fact]
    public void LowercaseVariable_WinsOverUppercase()
        => Select(Env(("https_proxy", "http://lower:1"), ("HTTPS_PROXY", "http://upper:2")))!.Host.ShouldBe("lower");

    /// <summary>★ <c>CONNECT</c> 隧道与 HTTPS 同形：<c>HTTPS_PROXY</c> 先于 <c>ALL_PROXY</c> 先于 <c>HTTP_PROXY</c>。</summary>
    [Theory]
    [InlineData("HTTPS_PROXY", "https-proxy")]
    [InlineData("ALL_PROXY", "all-proxy")]
    [InlineData("HTTP_PROXY", "http-proxy")]
    public void VariablesAreReadInOrder(string firstPresent, string expectedHost)
    {
        var all = new List<(string, string)>
        {
            ("HTTPS_PROXY", "http://https-proxy:1"),
            ("ALL_PROXY", "http://all-proxy:1"),
            ("HTTP_PROXY", "http://http-proxy:1"),
        };
        var present = all.SkipWhile(v => v.Item1 != firstPresent).ToArray();

        Select(Env(present))!.Host.ShouldBe(expectedHost);
    }

    /// <summary>大写 <c>HTTP_PROXY</c> 在 CGI 下不读（httpoxy）；小写照读。</summary>
    [Fact]
    public void UppercaseHttpProxy_IsIgnoredUnderCgi()
    {
        Select(Env(("REQUEST_METHOD", "GET"), ("HTTP_PROXY", "http://injected:1"))).ShouldBeNull();
        Select(Env(("REQUEST_METHOD", "GET"), ("http_proxy", "http://ok:1")))!.Host.ShouldBe("ok");
    }

    /// <summary>环境变量写坏了：看下一个，都不行就直连，不因此拒绝发信（与 <c>HttpClient</c> 一致）。</summary>
    [Fact]
    public void AnUnparsableVariable_FallsThrough()
    {
        Select(Env(("HTTPS_PROXY", "ftp://nope:21"), ("HTTP_PROXY", "http://fallback:3128")))!.Host.ShouldBe("fallback");
        Select(Env(("HTTPS_PROXY", "::::"))).ShouldBeNull();
    }

    /// <summary>★ <c>UseEnvironmentProxy = false</c>：环境怎么写都直连。</summary>
    [Fact]
    public void EnvironmentProxyTurnedOff_ConnectsDirectly()
        => Select(Env(("HTTPS_PROXY", "http://proxy:3128")), useEnvironment: false).ShouldBeNull();

    /// <summary>★★ 显式 <c>Proxy</c> 胜过环境变量，也不看 <c>NO_PROXY</c>、不看开关。</summary>
    [Fact]
    public void AConfiguredProxy_OverridesEverything()
    {
        var env = Env(("HTTPS_PROXY", "http://env:1"), ("NO_PROXY", SmtpHost));

        Select(env, configured: "socks5://configured:1080")!.Host.ShouldBe("configured");
        Select(env, configured: "http://configured:3128", useEnvironment: false)!.Host.ShouldBe("configured");
        Select(env, host: "localhost", configured: "http://configured:3128")!.Host.ShouldBe("configured");
    }

    /// <summary>显式 <c>Proxy</c> 写坏了是配置错误：报出来（凭据打码），不悄悄直连。</summary>
    [Fact]
    public void AnInvalidConfiguredProxy_Throws()
    {
        var ex = Should.Throw<InvalidOperationException>(() => Select(Env(), configured: "ftp://user:secret@proxy:21"));
        ex.Message.ShouldNotContain("secret");
    }

    // ── NO_PROXY 与回环 ────────────────────────────────────────────────────────

    /// <summary>★★ <c>NO_PROXY</c> 的各种写法。</summary>
    [Theory]
    [InlineData("*", SmtpHost, 587, true)]
    [InlineData("example.com", SmtpHost, 587, true)]
    [InlineData(".example.com", SmtpHost, 587, true)]
    [InlineData("*.example.com", SmtpHost, 587, true)]
    [InlineData("EXAMPLE.COM", "Smtp.Example.Com", 587, true)]
    [InlineData("smtp.example.com", SmtpHost, 587, true)]
    [InlineData(".example.com", "example.com", 587, true)]
    [InlineData("ample.com", SmtpHost, 587, false)]
    [InlineData("other.com, example.org", SmtpHost, 587, false)]
    [InlineData("other.com , example.com", SmtpHost, 587, true)]
    [InlineData("smtp.example.com:587", SmtpHost, 587, true)]
    [InlineData("smtp.example.com:465", SmtpHost, 587, false)]
    [InlineData("10.0.0.0/8", "10.1.2.3", 587, true)]
    [InlineData("10.0.0.0/8", "192.168.1.1", 587, false)]
    [InlineData("10.0.0.0/8", "smtp.10.example", 587, false)]
    [InlineData("192.168.1.10", "192.168.1.10", 587, true)]
    [InlineData("192.168.1.10", "192.168.1.11", 587, false)]
    [InlineData("::2", "[::2]", 587, true)]
    [InlineData("[::2]:587", "::2", 587, true)]
    [InlineData("", SmtpHost, 587, false)]
    public void NoProxyMatching(string noProxy, string host, int port, bool bypassed)
        => MailProxySelector.IsBypassed(host, port, noProxy).ShouldBe(bypassed);

    /// <summary>目标在 <c>NO_PROXY</c> 里：直连。<c>no_proxy</c> 先于 <c>NO_PROXY</c>。</summary>
    [Fact]
    public void ATargetInNoProxy_ConnectsDirectly()
    {
        Select(Env(("HTTPS_PROXY", "http://proxy:3128"), ("NO_PROXY", ".example.com"))).ShouldBeNull();
        Select(Env(("HTTPS_PROXY", "http://proxy:3128"), ("no_proxy", "other.org"), ("NO_PROXY", ".example.com"))).ShouldNotBeNull();
    }

    /// <summary>★ 回环地址从不经环境代理：代理那头的「本机」不是这台（本机开发用的 SMTP 桩、同 Pod 的中继）。</summary>
    [Theory]
    [InlineData("localhost")]
    [InlineData("mail.localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.0.1")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    public void Loopback_IsNeverProxiedFromTheEnvironment(string host)
        => Select(Env(("HTTPS_PROXY", "http://proxy:3128")), host: host).ShouldBeNull();

    // ── 代理地址的形状 ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://proxy:3128", "http", 3128)]
    [InlineData("proxy:3128", "http", 3128)]
    [InlineData("http://proxy", "http", 80)]
    [InlineData("https://proxy", "https", 443)]
    [InlineData("socks5://proxy", "socks5", -1)]
    [InlineData("socks5h://proxy:1081", "socks5h", 1081)]
    [InlineData("socks4a://proxy:1080", "socks4a", 1080)]
    [InlineData("  http://10.0.0.1:8080/  ", "http", 8080)]
    public void ProxyAddressesParse(string value, string scheme, int port)
    {
        MailProxySelector.TryParseProxy(value, out var proxy).ShouldBeTrue();
        proxy.Scheme.ShouldBe(scheme);
        proxy.Port.ShouldBe(port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://proxy:21")]
    [InlineData("http://")]
    [InlineData("::::")]
    public void UnsupportedProxyAddressesDoNotParse(string value)
        => MailProxySelector.TryParseProxy(value, out _).ShouldBeFalse();

    /// <summary>★ 协议决定 MailKit 的代理客户端；凭据取自地址并按 URL 编码解码；SOCKS 不写端口默认 1080。</summary>
    [Theory]
    [InlineData("http://proxy:3128", typeof(HttpProxyClient), 3128)]
    [InlineData("https://proxy", typeof(HttpsProxyClient), 443)]
    [InlineData("socks4://proxy", typeof(Socks4Client), 1080)]
    [InlineData("socks4a://proxy:9050", typeof(Socks4aClient), 9050)]
    [InlineData("socks5://proxy", typeof(Socks5Client), 1080)]
    [InlineData("socks5h://proxy:1081", typeof(Socks5Client), 1081)]
    public void TheSchemeChoosesTheProxyClient(string value, Type expected, int port)
    {
        MailProxySelector.TryParseProxy(value, out var proxy).ShouldBeTrue();
        var client = MailProxySelector.CreateClient(proxy);

        client.ShouldBeOfType(expected);
        client.ProxyHost.ShouldBe("proxy");
        client.ProxyPort.ShouldBe(port);
        client.ProxyCredentials.ShouldBeNull();
    }

    [Fact]
    public void CredentialsInTheAddress_AreDecoded()
    {
        MailProxySelector.TryParseProxy("http://mail%40corp:p%3Ass@proxy:3128", out var proxy).ShouldBeTrue();
        var credentials = MailProxySelector.CreateClient(proxy).ProxyCredentials;

        credentials.ShouldNotBeNull();
        credentials.UserName.ShouldBe("mail@corp");
        credentials.Password.ShouldBe("p:ss");
    }

    [Theory]
    [InlineData("http://user:secret@proxy:3128", "http://***@proxy:3128")]
    [InlineData("user:secret@proxy:3128", "***@proxy:3128")]
    [InlineData("http://proxy:3128", "http://proxy:3128")]
    public void RedactHidesCredentials(string value, string expected)
        => MailProxySelector.Redact(value).ShouldBe(expected);

    // ── 校验 ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Validation_RejectsAnInvalidMailProxy_WithoutLeakingTheSecret()
    {
        var result = new NotificationOptionsValidator().Validate(name: null, new NotificationOptions
        {
            MailSender = new MailSenderOptions { SmtpServer = SmtpHost, FromEmail = "app@example.com", EnableSsl = false, Proxy = "ftp://u:secret@proxy" },
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("MailSender.Proxy");
        result.FailureMessage.ShouldNotContain("secret");
    }

    [Fact]
    public void Validation_RejectsAnInvalidConfirmationProxy()
    {
        var result = new NotificationOptionsValidator().Validate(name: null, new NotificationOptions
        {
            FaxSender = new FaxSenderOptions
            {
                Enabled = true,
                GatewayDomain = "fax.example.com",
                Confirmation = new FaxConfirmationOptions { Host = "imap.example.com", UserName = "u", Password = "p", Proxy = "gopher://proxy" },
            },
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("FaxSender.Confirmation.Proxy");
    }

    [Fact]
    public void Validation_AcceptsAValidMailProxy()
        => new NotificationOptionsValidator().Validate(name: null, new NotificationOptions
        {
            MailSender = new MailSenderOptions { SmtpServer = SmtpHost, FromEmail = "app@example.com", EnableSsl = false, Proxy = "http://proxy:3128" },
        }).Succeeded.ShouldBeTrue();

    // ── 真经代理发一封 ─────────────────────────────────────────────────────────

    /// <summary>
    /// ★★★ 经 HTTP 代理发一封信：发送器对代理说 <c>CONNECT 主机名:端口</c>，主机名由代理解析
    /// （<c>.invalid</c> 在本机永远解析不了，经代理之外的任何路径都会失败），带上地址里的凭据，信照常投递。
    /// </summary>
    [Fact]
    public async Task ThroughAnHttpProxy_TheSenderTunnelsByHostName()
    {
        await using var relay = MailKitConnectionTests.SmtpRelayStub.Start();
        await using var proxy = ConnectProxyStub.Start(relay.Port);
        var sender = new MailKitEmailSender(
            new MailSenderOptions
            {
                SmtpServer = "smtp.unresolvable.invalid",
                SmtpPort = 587,
                EnableSsl = false,
                FromEmail = "app@example.com",
                Proxy = $"http://mailer:pw@127.0.0.1:{proxy.Port}",
            },
            new Mock<IHttpClientFactory>().Object,
            new Mock<ILogger<MailKitEmailSender>>().Object);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await sender.SendToAsync("reader@example.com", null, "Hello", "Body", isHtml: false, cancellationToken: timeout.Token);

        result.Success.ShouldBeTrue(result.FailureReason);
        proxy.RequestLine.ShouldBe("CONNECT smtp.unresolvable.invalid:587 HTTP/1.1");
        proxy.Headers.ShouldContain(h => h.Equals(
            "Proxy-Authorization: Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("mailer:pw")), StringComparison.OrdinalIgnoreCase));
        relay.Commands.ShouldContain(c => c.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 最小的 HTTP <c>CONNECT</c> 代理桩：记下请求行与请求头，答 200，然后把隧道接到本机的 SMTP 桩（不管请求的是哪个主机）。
    /// </summary>
    private sealed class ConnectProxyStub : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly int _upstreamPort;
        private readonly Task _serving;

        private ConnectProxyStub(TcpListener listener, int upstreamPort)
        {
            _listener = listener;
            _upstreamPort = upstreamPort;
            _serving = Task.Run(ServeAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public string? RequestLine { get; private set; }

        public ConcurrentQueue<string> Headers { get; } = new();

        public static ConnectProxyStub Start(int upstreamPort)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new ConnectProxyStub(listener, upstreamPort);
        }

        private async Task ServeAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await using var downstream = client.GetStream();

                // 逐字节读请求头：不能多读，隧道建立后的字节属于 SMTP。
                RequestLine = await ReadLineAsync(downstream);
                while (await ReadLineAsync(downstream) is { Length: > 0 } header)
                    Headers.Enqueue(header);

                using var upstreamClient = new TcpClient();
                await upstreamClient.ConnectAsync(IPAddress.Loopback, _upstreamPort);
                await using var upstream = upstreamClient.GetStream();

                var established = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n");
                await downstream.WriteAsync(established);

                await Task.WhenAny(downstream.CopyToAsync(upstream), upstream.CopyToAsync(downstream));
            }
            catch (Exception) when (!_listener.Server.IsBound)
            {
                // 监听已停：测试结束时的正常退出。
            }
            catch (IOException)
            {
                // 一端断开：隧道结束。
            }
        }

        private static async Task<string?> ReadLineAsync(Stream stream)
        {
            var line = new StringBuilder();
            var buffer = new byte[1];
            while (await stream.ReadAsync(buffer) == 1)
            {
                if (buffer[0] == '\n')
                    return line.ToString().TrimEnd('\r');
                line.Append((char)buffer[0]);
            }

            return line.Length > 0 ? line.ToString() : null;
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await Task.WhenAny(_serving, Task.Delay(TimeSpan.FromSeconds(5)));
        }
    }
}
