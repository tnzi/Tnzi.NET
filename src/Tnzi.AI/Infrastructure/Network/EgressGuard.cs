using System.Net;
using System.Net.Sockets;

namespace Tnzi.AI.Infrastructure.Network;

/// <summary>
/// SSRF egress guard - validates outbound URLs before HTTP requests are issued.
/// Blocks loopback, RFC1918 private, link-local, and IPv6 ULA/link-local ranges.
/// All resolved IPs are checked so DNS rebinding is mitigated.
/// </summary>
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
    public static async Task<string?> CheckAsync(Uri uri, CancellationToken ct = default)
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
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (SocketException)
        {
            // DNS resolution failure - block to fail secure
            return $"DNS resolution failed for '{host}'. Access denied.";
        }

        foreach (var address in addresses)
        {
            if (IsBlockedAddress(address))
                return $"Access to private or internal address '{address}' (host: {host}) is not allowed.";
        }

        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="address"/> is a blocked
    /// (loopback, private, link-local, or IPv4-mapped equivalent) address.
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

            // 0.0.0.0 - wildcard
            if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0)
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

            // IPv6 link-local: fe80::/10
            if (address.IsIPv6LinkLocal)
                return true;

            // IPv6 Unique Local Address: fc00::/7 (fd00::/8 and fc00::/8)
            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC) // covers fc00:: and fd00::
                return true;
        }

        return false;
    }
}
