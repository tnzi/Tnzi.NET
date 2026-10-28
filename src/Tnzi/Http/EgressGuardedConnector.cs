namespace Tnzi.Http;

/// <summary>
/// 出站连接的连接期守卫：解析一次 DNS、逐个校验、只连到校验过的那个地址。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EgressGuard"/> 在校验那一刻解析并检查目标主机，随后发请求的 <see cref="HttpClient"/>
/// 在建立连接时会<b>再解析一次</b>。一个 TTL 为 0、第一次答公网地址、第二次答 <c>169.254.169.254</c>
/// 或内网地址的域名（DNS rebinding）就这样通过检查而连进内网，回应原样交给调用方。
/// 本类把检查挪到连接这一刻：<see cref="ConnectAsync(SocketsHttpConnectionContext, CancellationToken)"/>
/// 自己解析主机，任何一个结果被 <see cref="EgressGuard"/> 的规则拒绝就整体拒绝，
/// 否则把套接字直接连到这一次解析出来、刚刚校验过的地址上，不再给第二次解析留位置。
/// TLS 仍按请求里的主机名做（SNI 与证书校验不受影响）。
/// </para>
/// <para>
/// 事先的 <see cref="EgressGuard.CheckAsync(string, CancellationToken)"/> 仍然有用：它在发请求之前给出一句
/// 可读的拒绝原因。两者是两道关，不是二选一。
/// </para>
/// <para>
/// ★ <see cref="CreateHandler"/> 不跟随重定向（第二跳不经任何检查）也<b>不走代理</b>：经代理时连接的是代理，
/// 目标主机由代理去解析，这里看不到也钉不住它。需要经代理出网的部署，SSRF 防护只能放在代理那一侧。
/// </para>
/// </remarks>
public static class EgressGuardedConnector
{
    /// <summary>
    /// 创建一个连接期受 <see cref="EgressGuard"/> 约束的 <see cref="SocketsHttpHandler"/>：
    /// 不跟随重定向、不走代理，每条连接都连到当场校验过的地址。
    /// </summary>
    /// <remarks>
    /// 给按运行时输入的 URL 发请求的具名客户端用：
    /// <c>services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(EgressGuardedConnector.CreateHandler)</c>。
    /// </remarks>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectCallback = ConnectAsync,
    };

    /// <summary>
    /// 可直接挂到自建 <see cref="SocketsHttpHandler.ConnectCallback"/> 上的连接回调。
    /// </summary>
    /// <exception cref="HttpRequestException">目标主机解析失败、没有地址，或任一地址落在被拒绝的范围内。</exception>
    public static ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        Check.NotNull(context);
        return ConnectAsync(context.DnsEndPoint, ResolveAsync, cancellationToken);
    }

    /// <summary>
    /// 连接本体；解析函数可替换，测试借此模拟一个两次解析答案不同的域名。
    /// </summary>
    internal static async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endPoint,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        CancellationToken cancellationToken)
    {
        Check.NotNull(endPoint);
        Check.NotNull(resolve);

        var addresses = await ResolveAndCheckAsync(endPoint.Host, resolve, cancellationToken);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 解析一次并逐个校验；返回的就是随后要连的那组地址。
    /// </summary>
    /// <remarks>
    /// 口径与 <see cref="EgressGuard"/> 相同：任一地址被拒绝就整体拒绝（只挑「好的那个」连，等于让攻击者
    /// 用一条公网记录给一条内网记录打掩护），解析失败一律拒绝。IP 字面量不经 DNS，直接判定。
    /// </remarks>
    private static async Task<IPAddress[]> ResolveAndCheckAsync(
        string host,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await resolve(host, cancellationToken);
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                throw new HttpRequestException(
                    HttpRequestError.NameResolutionError, $"DNS resolution failed for '{host}'. Access denied.", ex);
            }
        }

        if (addresses.Length == 0)
        {
            throw new HttpRequestException(
                HttpRequestError.NameResolutionError, $"DNS resolution returned no addresses for '{host}'. Access denied.");
        }

        foreach (var address in addresses)
        {
            if (EgressGuard.IsBlockedAddress(address))
            {
                throw new HttpRequestException(
                    HttpRequestError.ConnectionError, $"Access to private or internal address '{address}' (host: {host}) is not allowed.");
            }
        }

        return addresses;
    }

    private static Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        => Dns.GetHostAddressesAsync(host, cancellationToken);
}
