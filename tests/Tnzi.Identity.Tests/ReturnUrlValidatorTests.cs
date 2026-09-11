namespace Tnzi.Identity.Tests;

/// <summary>
/// <c>returnUrl</c> 白名单。
/// </summary>
/// <remarks>
/// 守的是一条完整的接管路径：OAuth 回调页在没有 opener 时执行
/// <c>location.href = new URL(returnUrl, origin) + "#accessToken=…&amp;refreshToken=…"</c>，
/// 而绝对地址会盖掉基地址 —— 于是一条
/// <c>/auth/oauth/google/login?returnUrl=https://evil.example</c> 就把受害者的
/// 访问令牌与刷新令牌送了出去（受害者在第三方那边通常已授权，整个流程静默完成）。
/// </remarks>
public class ReturnUrlValidatorTests
{
    private static readonly string[] Allowed = ["https://app.example.com"];

    [Fact]
    public void NullOrBlank_IsAllowed()
    {
        // 「没给」不是「给了个坏的」，由调用方决定要不要跳转。
        ReturnUrlValidator.IsAllowed(null, Allowed).ShouldBeTrue();
        ReturnUrlValidator.IsAllowed("   ", Allowed).ShouldBeTrue();
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/")]
    [InlineData("/a/b?c=d#e")]
    public void SiteRelativePath_IsAllowed(string url)
        => ReturnUrlValidator.IsAllowed(url, Allowed).ShouldBeTrue();

    [Fact]
    public void SiteRelativePath_IsAllowed_EvenWithNoWhitelist()
        // 没配白名单 ≠ 放行一切：站内路径仍然可以，绝对地址一律不行。
        => ReturnUrlValidator.IsAllowed("/dashboard", []).ShouldBeTrue();

    [Fact]
    public void AbsoluteUrl_WithoutWhitelist_IsRejected()
        => ReturnUrlValidator.IsAllowed("https://app.example.com/x", []).ShouldBeFalse();

    [Fact]
    public void AbsoluteUrl_OnWhitelistedOrigin_IsAllowed()
        => ReturnUrlValidator.IsAllowed("https://app.example.com/callback?x=1", Allowed).ShouldBeTrue();

    /// <summary>★ 这一条就是那次接管：目标域不在白名单上。</summary>
    [Fact]
    public void AbsoluteUrl_OnForeignOrigin_IsRejected()
        => ReturnUrlValidator.IsAllowed("https://evil.example/steal", Allowed).ShouldBeFalse();

    [Theory]
    [InlineData("http://app.example.com/x")]      // scheme 不同
    [InlineData("https://app.example.com:8443/x")] // 端口不同
    [InlineData("https://evil-app.example.com/x")] // host 不同（且是前缀式的近似域名）
    public void OriginComparison_IsExact(string url)
        => ReturnUrlValidator.IsAllowed(url, Allowed).ShouldBeFalse();

    /// <summary>
    /// ★★★ 协议相对地址：按字符串看「以 / 开头」，浏览器却当成绝对地址跳出站外。
    /// 只判首字符是 <c>/</c> 的实现会在这里放行。
    /// </summary>
    [Theory]
    [InlineData("//evil.example")]
    [InlineData("//evil.example/path")]
    public void ProtocolRelativeUrl_IsRejected(string url)
        => ReturnUrlValidator.IsAllowed(url, Allowed).ShouldBeFalse();

    /// <summary>
    /// ★★ 反斜杠：若干浏览器在若干位置把 <c>\</c> 当 <c>/</c>，
    /// 于是 <c>/\evil.example</c> 与 <c>\\evil.example</c> 也可能被解析成协议相对地址。
    /// </summary>
    [Theory]
    [InlineData(@"/\evil.example")]
    [InlineData(@"\\evil.example")]
    [InlineData(@"/path\..\..")]
    public void BackslashForms_AreRejected(string url)
        => ReturnUrlValidator.IsAllowed(url, Allowed).ShouldBeFalse();

    /// <summary>可执行的 scheme —— <c>location.href</c> 会真的运行它。</summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>1</script>")]
    [InlineData("file:///etc/passwd")]
    public void NonHttpSchemes_AreRejected(string url)
        => ReturnUrlValidator.IsAllowed(url, ["https://app.example.com", "javascript:x"]).ShouldBeFalse();

    /// <summary>控制字符与空白会让「这里怎么解析」与「浏览器怎么解析」分叉，绕过都住在那条缝里。</summary>
    [Theory]
    [InlineData("/path with space")]
    [InlineData("/path\nX")]
    [InlineData("/path\tX")]
    public void ControlCharactersAndWhitespace_AreRejected(string url)
        => ReturnUrlValidator.IsAllowed(url, Allowed).ShouldBeFalse();

    [Fact]
    public void ResolveAllowedOrigins_PrefersConfiguredList()
        => ReturnUrlValidator.ResolveAllowedOrigins(["https://a.example"], "https://b.example")
            .ShouldBe(["https://a.example"]);

    [Fact]
    public void ResolveAllowedOrigins_FallsBackToFrontendUrl()
        // 绝大多数部署不必单独配这一项：App:FrontendUrl 本来就指着前端所在的源。
        => ReturnUrlValidator.ResolveAllowedOrigins([], "https://b.example")
            .ShouldBe(["https://b.example"]);

    [Fact]
    public void ResolveAllowedOrigins_EmptyWhenNeitherConfigured()
        => ReturnUrlValidator.ResolveAllowedOrigins(null, null).ShouldBeEmpty();
}
