using Tnzi.Http;

namespace Tnzi.Tests.Http;

/// <summary>
/// EgressGuard 单元测试 - 验证 SSRF 防护逻辑覆盖 IPv4/IPv6/mapped 地址
/// </summary>
public class EgressGuardTests
{
    // ------------------------------------------------------------------
    // CheckAsync - scheme validation (synchronous path, no DNS needed)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("ftp://example.com/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ldap://internal/")]
    public async Task CheckAsync_NonHttpScheme_ReturnsError(string url)
    {
        var error = await EgressGuard.CheckAsync(url);
        Assert.NotNull(error);
        Assert.Contains("not allowed", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckAsync_EmptyUrl_ReturnsError()
    {
        var error = await EgressGuard.CheckAsync(string.Empty);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CheckAsync_InvalidUrl_ReturnsError()
    {
        var error = await EgressGuard.CheckAsync("not-a-url");
        Assert.NotNull(error);
    }

    // ------------------------------------------------------------------
    // CheckAsync - loopback and link-local addresses (resolvable without real DNS)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("http://127.0.0.1/path")]     // IPv4 loopback - always resolves
    [InlineData("http://[::1]/path")]          // IPv6 loopback - always resolves
    public async Task CheckAsync_LoopbackAddress_ReturnsError(string url)
    {
        var error = await EgressGuard.CheckAsync(url);
        Assert.NotNull(error);
    }

    // ------------------------------------------------------------------
    // CheckAsync - IPv4 literal private addresses
    // These are IP literals in the URL so no DNS lookup is needed.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")] // AWS/Azure cloud metadata
    [InlineData("http://10.0.0.1/internal")]                  // RFC1918 10/8
    [InlineData("http://172.16.0.1/private")]                 // RFC1918 172.16/12
    [InlineData("http://192.168.1.1/router")]                 // RFC1918 192.168/16
    public async Task CheckAsync_PrivateIpLiteral_ReturnsError(string url)
    {
        var error = await EgressGuard.CheckAsync(url);
        Assert.NotNull(error);
        // Error message should mention blocked/allowed/private
        Assert.True(
            error.Contains("Access", StringComparison.OrdinalIgnoreCase)
            || error.Contains("DNS", StringComparison.OrdinalIgnoreCase),
            "the error must name the blocked access or the failed DNS resolution");
    }

    // ------------------------------------------------------------------
    // CheckAsync - public addresses should NOT be blocked at the scheme-check stage
    // (We do NOT assert null here since DNS might fail in CI; we only assert
    //  the scheme was accepted by checking the error doesn't mention the scheme.)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("https://8.8.8.8/")]
    [InlineData("https://example.com/")]
    public async Task CheckAsync_HttpsScheme_SchemeNotRejected(string url)
    {
        var error = await EgressGuard.CheckAsync(url);
        // If blocked, it must NOT be because of the scheme
        if (error != null)
        {
            Assert.DoesNotContain("Scheme", error, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------
    // FIX 2 (a): hostname that DNS-resolves to a private IP - DNS path
    // "localhost" is guaranteed to resolve to 127.0.0.1 or ::1 on every OS.
    // This proves the DNS-resolution code path, not just literal-IP checks.
    // ------------------------------------------------------------------

    [Fact]
    public async Task CheckAsync_HostnameThatResolvesToLoopback_ReturnsError()
    {
        // "localhost" resolves to 127.0.0.1 and/or ::1 - both are loopback
        var error = await EgressGuard.CheckAsync("http://localhost/path");
        Assert.True(error != null, "localhost must be blocked because it resolves to a loopback address");
    }

    // ------------------------------------------------------------------
    // FIX 2 (b): IPv4-mapped IPv6 literals in URLs - e.g. [::ffff:127.0.0.1]
    // Proves the IsIPv4MappedToIPv6 → MapToIPv4 → IsBlockedAddress recursion
    // through the public CheckAsync API.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("http://[::ffff:127.0.0.1]/")]          // mapped loopback
    [InlineData("http://[::ffff:169.254.169.254]/")]    // mapped link-local / cloud metadata
    [InlineData("http://[::ffff:10.0.0.1]/")]           // mapped RFC1918
    public async Task CheckAsync_IPv4MappedIPv6Literal_ReturnsError(string url)
    {
        var error = await EgressGuard.CheckAsync(url);
        Assert.True(error != null, $"IPv4-mapped IPv6 address in {url} must be blocked");
    }

    // ------------------------------------------------------------------
    // 未指定地址、内嵌 IPv4 的 IPv6 形态、多播与广播：连接这些地址要么落到本机，
    // 要么落到一段内网 IPv4 上。字面量必须给出拒绝原因而不是抛异常（抛出去调用方回 500 而不是 400）。
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("http://[::]/")]                       // IPv6 未指定地址：Linux 上连接会落到本机
    [InlineData("http://0.0.0.0/")]                    // IPv4 未指定地址
    [InlineData("http://0.1.2.3/")]                    // 0.0.0.0/8 整段
    [InlineData("http://[::7f00:1]/")]                 // IPv4 兼容地址（::/96）内嵌 127.0.0.1
    [InlineData("http://[64:ff9b::a00:1]/")]           // NAT64 前缀内嵌 10.0.0.1
    [InlineData("http://[64:ff9b::a9fe:a9fe]/")]       // NAT64 前缀内嵌 169.254.169.254
    [InlineData("http://224.0.0.1/")]                  // IPv4 多播
    [InlineData("http://255.255.255.255/")]            // 受限广播
    [InlineData("http://[ff02::1]/")]                  // IPv6 多播
    [InlineData("http://[fec0::1]/")]                  // IPv6 站点本地（已废弃，仍有协议栈路由）
    public async Task CheckAsync_UnspecifiedEmbeddedOrMulticastLiteral_ReturnsReasonWithoutThrowing(string url)
    {
        var error = await EgressGuard.CheckAsync(url);
        Assert.True(error != null, $"{url} must be blocked");
    }

    [Theory]
    [InlineData("http://[::]:6379/")]
    [InlineData("http://0.0.0.0:6379/")]
    public async Task CheckUriAsync_UnspecifiedLiteral_IsRejectedAsInternalNotAsDnsFailure(string url)
    {
        var error = await EgressGuard.CheckUriAsync(new Uri(url));
        Assert.NotNull(error);
        Assert.Contains("private or internal address", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://8.8.8.8/")]                   // 公网 IPv4 字面量不经 DNS 也放行
    [InlineData("https://[2001:4860:4860::8888]/")]    // 公网 IPv6 字面量
    [InlineData("https://[64:ff9b::808:808]/")]        // NAT64 内嵌公网 8.8.8.8
    public async Task CheckAsync_PublicLiteral_IsAllowed(string url)
    {
        Assert.Null(await EgressGuard.CheckAsync(url));
    }
}
