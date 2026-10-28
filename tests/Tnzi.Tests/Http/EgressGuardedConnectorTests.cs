using System.Net;
using System.Net.Sockets;
using Tnzi.Http;

namespace Tnzi.Tests.Http;

/// <summary>
/// 连接期出站守卫：解析一次、逐个校验、只连到校验过的地址。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：<see cref="EgressGuard"/> 在校验那一刻解析并检查主机，而 <see cref="HttpClient"/>
/// 建立连接时会再解析一次。一个 TTL 为 0、第一次答公网地址、第二次答云元数据或内网地址的域名，
/// 就这样通过检查连进内网（远程附件、Webhook 一类「按运行时 URL 取内容」的入口，回应原样交给调用方）。
/// </para>
/// <para>
/// 用例在本机回环上开一个监听端口当作「内网服务」：守卫生效时它一个连接都收不到。
/// 「两次解析答案不同」用可替换的解析函数造：它答公网地址，而系统 DNS 会把同一个主机名（<c>localhost</c>）答成回环。
/// 守卫若只是事先校验、连接时再交给系统解析，连接就会落到那个监听端口上。
/// </para>
/// </remarks>
public sealed class EgressGuardedConnectorTests : IDisposable
{
    /// <summary>TEST-NET-3（RFC 5737）：公网地址形态、不会被路由，连接只会超时或被拒。</summary>
    private static readonly IPAddress PublicDocumentationAddress = IPAddress.Parse("203.0.113.10");

    private readonly TcpListener _internalService = new(IPAddress.Loopback, 0);

    /// <summary>
    /// 同一端口在 IPv6 回环上也开一个：系统把 <c>localhost</c> 先答成 <c>::1</c>，
    /// 而 Windows 上连一个没人监听的回环端口要重试两秒左右，只听 IPv4 的话「改连到回环」会先超时而测不出来。
    /// </summary>
    private readonly TcpListener? _internalServiceV6;

    public EgressGuardedConnectorTests()
    {
        _internalService.Start();
        if (Socket.OSSupportsIPv6)
        {
            _internalServiceV6 = new TcpListener(IPAddress.IPv6Loopback, InternalPort);
            _internalServiceV6.Start();
        }
    }

    private int InternalPort => ((IPEndPoint)_internalService.LocalEndpoint).Port;

    private bool InternalServiceWasReached => _internalService.Pending() || (_internalServiceV6?.Pending() ?? false);

    [Fact]
    public async Task AHostResolvingToABlockedAddress_IsRefusedBeforeConnecting()
    {
        var resolver = new CountingResolver(IPAddress.Loopback);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            EgressGuardedConnector.ConnectAsync(new DnsEndPoint("rebind.example", InternalPort), resolver.ResolveAsync, CancellationToken.None).AsTask());

        Assert.Contains("not allowed", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, resolver.Calls);
        Assert.False(InternalServiceWasReached);
    }

    /// <summary>
    /// ★ 本类存在的理由：连接打到的是这一次解析出来、刚刚校验过的地址，而不是另一次解析的结果。
    /// </summary>
    [Fact]
    public async Task TheConnectionGoesToTheCheckedAddress_NotToASecondResolution()
    {
        var resolver = new CountingResolver(PublicDocumentationAddress);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        try
        {
            // 系统 DNS 会把 localhost 答成回环，正是那个「内网服务」所在
            await using var stream = await EgressGuardedConnector.ConnectAsync(
                new DnsEndPoint("localhost", InternalPort), resolver.ResolveAsync, timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException)
        {
            // 连不上那个文档地址是预期的：要证明的只是它没有改连到回环
        }

        Assert.Equal(1, resolver.Calls);
        Assert.False(InternalServiceWasReached, "The connection was made to a second resolution of the host, not to the address that was checked.");
    }

    /// <summary>一条公网记录不能给一条内网记录打掩护：任一地址被拒绝，整个主机拒绝。</summary>
    [Fact]
    public async Task OneBlockedAddressAmongSeveral_RefusesTheHost()
    {
        var resolver = new CountingResolver(PublicDocumentationAddress, IPAddress.Parse("10.0.0.5"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            EgressGuardedConnector.ConnectAsync(new DnsEndPoint("mixed.example", InternalPort), resolver.ResolveAsync, CancellationToken.None).AsTask());
    }

    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    public async Task AnIpLiteralHost_IsJudgedWithoutResolving(string host)
    {
        var resolver = new CountingResolver();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            EgressGuardedConnector.ConnectAsync(new DnsEndPoint(host, InternalPort), resolver.ResolveAsync, CancellationToken.None).AsTask());

        Assert.Equal(0, resolver.Calls);
        Assert.False(InternalServiceWasReached);
    }

    [Fact]
    public async Task AResolutionFailure_IsRefused()
    {
        static Task<IPAddress[]> Failing(string host, CancellationToken ct) => throw new SocketException((int)SocketError.HostNotFound);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            EgressGuardedConnector.ConnectAsync(new DnsEndPoint("missing.example", InternalPort), Failing, CancellationToken.None).AsTask());

        Assert.Equal(HttpRequestError.NameResolutionError, ex.HttpRequestError);
    }

    [Fact]
    public async Task AnEmptyAnswer_IsRefused()
    {
        var resolver = new CountingResolver();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            EgressGuardedConnector.ConnectAsync(new DnsEndPoint("empty.example", InternalPort), resolver.ResolveAsync, CancellationToken.None).AsTask());
    }

    /// <summary>
    /// 端到端：<see cref="EgressGuardedConnector.CreateHandler"/> 造的客户端连不上回环上的服务，
    /// 同时不跟随重定向、不走代理（经代理时目标由代理解析，这里钉不住它）。
    /// </summary>
    [Fact]
    public async Task TheHandler_RefusesAnInternalTarget_AndNeitherRedirectsNorProxies()
    {
        using var handler = EgressGuardedConnector.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.NotNull(handler.ConnectCallback);

        using var client = new HttpClient(handler, disposeHandler: false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://127.0.0.1:{InternalPort}/", timeout.Token));

        Assert.False(InternalServiceWasReached);
    }

    public void Dispose()
    {
        _internalService.Stop();
        _internalServiceV6?.Stop();
    }

    private sealed class CountingResolver(params IPAddress[] answer)
    {
        public int Calls { get; private set; }

        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(answer);
        }
    }
}
