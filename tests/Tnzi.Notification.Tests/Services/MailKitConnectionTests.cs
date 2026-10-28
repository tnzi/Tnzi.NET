using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// <see cref="MailKitEmailSender"/> 与 SMTP 服务器怎么连、要不要认证：对着一个进程内的 SMTP 桩验证。
/// </summary>
/// <remarks>
/// ★★ <b>补的是哪个洞。</b>配置校验与文档都说「不启用 SSL 时用户名密码可以不填」，于是一个不带认证的内网中继
/// （端口 25、<c>EnableSsl = false</c>、无凭据）是一份合法配置。而发送器连上之后<b>无条件</b> <c>AUTH</c>：
/// 服务器不宣告认证时 MailKit 当场抛「The SMTP server does not support authentication」，每一封信都记成失败，
/// 包括密码重置。另一半是 465 端口：它要求连上就握 TLS，而发送器只会 STARTTLS，两边互等到超时。
/// </remarks>
public class MailKitConnectionTests
{
    /// <summary>★★★ 不带认证的中继：不填用户名就不做 AUTH，信照常投递。</summary>
    [Fact]
    public async Task AnUnauthenticatedRelay_DeliversWithoutAuth()
    {
        await using var relay = SmtpRelayStub.Start();
        var sender = NewSender(new MailSenderOptions
        {
            SmtpServer = "127.0.0.1",
            SmtpPort = relay.Port,
            EnableSsl = false,
            FromEmail = "app@example.com",
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var result = await sender.SendToAsync("reader@example.com", null, "Hello", "Body", isHtml: false, cancellationToken: timeout.Token);

        result.Success.ShouldBeTrue(result.FailureReason);
        relay.Commands.ShouldContain(c => c.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase));
        relay.Commands.ShouldNotContain(c => c.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase));
    }

    // ── 连接方式 ─────────────────────────────────────────────────────────────

    /// <summary>不启用 SSL：明文连接。</summary>
    [Fact]
    public void WithoutSsl_TheConnectionIsPlain()
        => MailKitEmailSender.SocketOptionsFor(new MailSenderOptions { EnableSsl = false, SmtpPort = 25 })
            .ShouldBe(MailKit.Security.SecureSocketOptions.None);

    /// <summary>★ 465 是隐式 TLS 端口：连上就握 TLS。</summary>
    [Fact]
    public void Port465_UsesTlsOnConnect()
        => MailKitEmailSender.SocketOptionsFor(new MailSenderOptions { EnableSsl = true, SmtpPort = 465 })
            .ShouldBe(MailKit.Security.SecureSocketOptions.SslOnConnect);

    /// <summary>
    /// ★★ 其余端口一律<b>必须</b> STARTTLS：不能是「有就用」（<c>Auto</c> / <c>StartTlsWhenAvailable</c>）：
    /// 服务器不宣告 STARTTLS 时那两种会悄悄退回明文，凭据随之明文发出去。
    /// </summary>
    [Theory]
    [InlineData(587)]
    [InlineData(25)]
    [InlineData(2525)]
    public void OtherPortsWithSsl_RequireStartTls(int port)
        => MailKitEmailSender.SocketOptionsFor(new MailSenderOptions { EnableSsl = true, SmtpPort = port })
            .ShouldBe(MailKit.Security.SecureSocketOptions.StartTls);

    /// <summary>用户名为空不认证；填了才认证。</summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("mailer", true)]
    public void AuthenticationFollowsTheUserName(string userName, bool expected)
        => MailKitEmailSender.ShouldAuthenticate(new MailSenderOptions { Username = userName }).ShouldBe(expected);

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private static MailKitEmailSender NewSender(MailSenderOptions options)
        => new(options, new Mock<IHttpClientFactory>().Object, new Mock<ILogger<MailKitEmailSender>>().Object);

    /// <summary>
    /// 最小的 SMTP 中继桩：只宣告 8BITMIME（不宣告 AUTH，也不宣告 STARTTLS），收一封信，记下每一条命令。
    /// </summary>
    private sealed class SmtpRelayStub : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _serving;

        private SmtpRelayStub(TcpListener listener)
        {
            _listener = listener;
            _serving = Task.Run(ServeAsync);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public ConcurrentQueue<string> Commands { get; } = new();

        public static SmtpRelayStub Start()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new SmtpRelayStub(listener);
        }

        private async Task ServeAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

                await writer.WriteLineAsync("220 relay.test ESMTP");
                while (await reader.ReadLineAsync() is { } line)
                {
                    Commands.Enqueue(line);
                    var verb = line.Split(' ', 2)[0].ToUpperInvariant();
                    switch (verb)
                    {
                        case "EHLO":
                            await writer.WriteLineAsync("250-relay.test");
                            await writer.WriteLineAsync("250 8BITMIME");
                            break;
                        case "DATA":
                            await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                            while (await reader.ReadLineAsync() is { } data && data != ".")
                            {
                            }
                            await writer.WriteLineAsync("250 2.0.0 queued");
                            break;
                        case "QUIT":
                            await writer.WriteLineAsync("221 2.0.0 bye");
                            return;
                        case "AUTH":
                            await writer.WriteLineAsync("502 5.5.1 authentication not available");
                            break;
                        default:
                            await writer.WriteLineAsync("250 2.0.0 ok");
                            break;
                    }
                }
            }
            catch (Exception) when (!_listener.Server.IsBound)
            {
                // 监听已停：测试结束时的正常退出。
            }
            catch (IOException)
            {
                // 客户端提前断开：交给断言去说明发生了什么。
            }
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            await Task.WhenAny(_serving, Task.Delay(TimeSpan.FromSeconds(5)));
        }
    }
}
