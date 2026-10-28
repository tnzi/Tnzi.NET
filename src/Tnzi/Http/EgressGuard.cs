
namespace Tnzi.Http;

/// <summary>
/// SSRF egress guard - validates outbound URLs before HTTP requests are issued.
/// Blocks loopback, unspecified, RFC1918 private, CGN, link-local, multicast/broadcast,
/// IPv6 ULA/link-local/site-local ranges, and IPv6 forms that embed one of those IPv4
/// addresses (IPv4-mapped, IPv4-compatible, NAT64 64:ff9b::/96).
/// </summary>
/// <remarks>
/// <para>
/// <b>检查发生在校验那一刻，不钉定连接地址。</b>它解析一次 DNS、逐个检查全部结果后返回；调用方随后用原 URL
/// 发请求，<see cref="System.Net.Http.HttpClient"/> 在连接时会<b>再解析一次</b>。一个 TTL 为 0、先答公网地址
/// 再答内网地址的域名（DNS rebinding）能通过这里的检查而连到内网，本类不防这一类攻击。
/// 检查全部解析结果挡住的是「同时答公网与内网地址」与「直接指向内网」的形态。
/// </para>
/// <para>
/// IP 字面量不经 DNS，直接判定；任何解析失败都按拒绝处理并返回原因，从不抛给调用方。
/// </para>
/// </remarks>
public static class EgressGuard
{
    /// <summary>
    /// Validates <paramref name="url"/> synchronously at the string level (scheme + no DNS),
    /// then resolves DNS and checks all returned addresses.
    /// Returns a non-null error message when the request should be blocked.
    /// </summary>
    public static async Task<string?> CheckAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "URL must not be empty.";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return $"Invalid URL: {url}";

        // Only http/https are allowed
        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return $"Scheme '{uri.Scheme}' is not allowed. Only http and https are permitted.";
        }

        return await CheckHostAsync(uri.Host, ct);
    }

    /// <summary>
    /// Validates a pre-parsed <see cref="Uri"/>.
    /// Returns a non-null error message when the request should be blocked.
    /// </summary>
    /// <remarks>
    /// 刻意与 <see cref="CheckAsync(string, CancellationToken)"/> 分开命名：两者都带可选的
    /// <c>ct</c>，同名重载会让日后给任一方加可选参数变成二进制破坏（RS0026），
    /// 与 <c>DataScopeExtensions.CanAccessByIdAsync</c> 同一处理。
    /// </remarks>
    public static async Task<string?> CheckUriAsync(Uri uri, CancellationToken ct = default)
    {
        Check.NotNull(uri);

        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return $"Scheme '{uri.Scheme}' is not allowed. Only http and https are permitted.";
        }

        return await CheckHostAsync(uri.Host, ct);
    }

    /// <summary>
    /// Resolves <paramref name="host"/> and checks all returned IP addresses.
    /// </summary>
    private static async Task<string?> CheckHostAsync(string host, CancellationToken ct)
    {
        // IP 字面量直接判定：Dns.GetHostAddressesAsync 对未指定地址字面量（0.0.0.0、::）抛 ArgumentException，
        // 抛出去的结果是调用方回 500 而不是给出拒绝原因
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            return IsBlockedAddress(literal)
                ? $"Access to private or internal address '{literal}' (host: {host}) is not allowed."
                : null;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            // DNS resolution failure - block to fail secure
            return $"DNS resolution failed for '{host}'. Access denied.";
        }

        if (addresses.Length == 0)
            return $"DNS resolution returned no addresses for '{host}'. Access denied.";

        foreach (var address in addresses)
        {
            if (IsBlockedAddress(address))
                return $"Access to private or internal address '{address}' (host: {host}) is not allowed.";
        }

        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="address"/> is a blocked
    /// (loopback, unspecified, private, link-local, multicast, or an IPv6 form embedding such an IPv4) address.
    /// SSRF guard for outbound HTTP requests.
    /// </summary>
    internal static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        // ::ffff:x.x.x.x - extract the embedded IPv4 and recurse
        if (address.IsIPv4MappedToIPv6)
            return IsBlockedAddress(address.MapToIPv4());

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            // "This network" 0.0.0.0/8 (0.0.0.0 itself connects to the local host on Linux)
            if (bytes[0] == 0)
                return true;

            // Multicast 224.0.0.0/4, reserved 240.0.0.0/4 and limited broadcast 255.255.255.255
            if (bytes[0] >= 224)
                return true;

            // RFC1918: 10.0.0.0/8
            if (bytes[0] == 10)
                return true;

            // RFC1918: 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;

            // RFC1918: 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;

            // Link-local (cloud metadata etc.): 169.254.0.0/16
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;

            // Shared address space (CGN): 100.64.0.0/10
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // ::1 loopback already covered by IPAddress.IsLoopback above

            var bytes = address.GetAddressBytes();

            // IPv4-compatible ::/96 (deprecated): ::a.b.c.d. The unspecified address :: falls in here too
            // (embedded 0.0.0.0, blocked by the IPv4 rules; Linux connects :: to the local host).
            if (bytes.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0)
                return IsEmbeddedIPv4Blocked(bytes);

            // NAT64 well-known prefix 64:ff9b::/96: a DNS64 resolver synthesises these for IPv4-only names,
            // and the NAT64 gateway forwards them to the embedded IPv4
            if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xff && bytes[3] == 0x9b
                && bytes.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0)
                return IsEmbeddedIPv4Blocked(bytes);

            // IPv6 link-local: fe80::/10
            if (address.IsIPv6LinkLocal)
                return true;

            // IPv6 site-local: fec0::/10 (deprecated, still routed by some stacks)
            if (address.IsIPv6SiteLocal)
                return true;

            // IPv6 multicast: ff00::/8
            if (address.IsIPv6Multicast)
                return true;

            // IPv6 Unique Local Address: fc00::/7 (fd00::/8 and fc00::/8)
            if ((bytes[0] & 0xFE) == 0xFC) // covers fc00:: and fd00::
                return true;
        }

        return false;
    }

    /// <summary>The IPv4 address carried in the last 32 bits of an IPv6 address, judged by the IPv4 rules.</summary>
    private static bool IsEmbeddedIPv4Blocked(byte[] ipv6Bytes)
        => IsBlockedAddress(new IPAddress(ipv6Bytes.AsSpan(12, 4)));
}
