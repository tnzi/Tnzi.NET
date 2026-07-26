using Tnzi.AI.Infrastructure.Network;

namespace Tnzi.AI.Tests;

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
        error.ShouldNotBeNull();
        error.ShouldContain("not allowed");
    }

    [Fact]
    public async Task CheckAsync_EmptyUrl_ReturnsError()
    {
        var error = await EgressGuard.CheckAsync(string.Empty);
        error.ShouldNotBeNull();
    }

    [Fact]
    public async Task CheckAsync_InvalidUrl_ReturnsError()
    {
        var error = await EgressGuard.CheckAsync("not-a-url");
        error.ShouldNotBeNull();
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
        error.ShouldNotBeNull();
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
        error.ShouldNotBeNull();
        // Error message should mention blocked/allowed/private
        (error.Contains("Access", StringComparison.OrdinalIgnoreCase)
            || error.Contains("DNS", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
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
            error.ShouldNotContain("Scheme");
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
        error.ShouldNotBeNull("localhost must be blocked because it resolves to a loopback address");
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
        error.ShouldNotBeNull($"IPv4-mapped IPv6 address in {url} must be blocked");
    }
}
