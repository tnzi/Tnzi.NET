using Tnzi.Payment.Options;
using Tnzi.Payment.Services;

namespace Tnzi.Payment.Tests;

/// <summary>
/// 回跳地址准入判定。
/// </summary>
/// <remarks>
/// 调用方给的 <c>ReturnUrl</c> / <c>CancelUrl</c> 会被原样交给支付渠道，作为付款完成后
/// 把**付款人的浏览器**送去的地方。此前全仓没有任何允许列表 —— 那是一个开放重定向，
/// 而且是最贵的那一种：跳转发生在渠道支付页之后，付款人刚输完卡号，
/// 落在一个仿冒的「订单完成」页上不会有任何怀疑，而链接本身来自本商户的域名与下单接口。
/// </remarks>
public class PaymentRedirectPolicyTests
{
    private static PaymentOptions Options(string? defaultReturnUrl = null, params string[] allowed) => new()
    {
        DefaultReturnUrl = defaultReturnUrl,
        AllowedRedirectHosts = [.. allowed]
    };

    [Fact]
    public void NoUrlSupplied_IsAllowed()
    {
        // 「没指定」不是「指定了一个坏的」：服务端会回退到默认地址
        PaymentRedirectPolicy.IsAllowed(null, Options()).ShouldBeTrue();
        PaymentRedirectPolicy.IsAllowed("   ", Options()).ShouldBeTrue();
    }

    [Fact]
    public void WithNothingConfigured_EveryCallerSuppliedUrlIsRefused()
    {
        // 失败关闭。放行等于保留现状，而「没人配」恰恰是默认状态。
        PaymentRedirectPolicy.IsAllowed("https://shop.example.com/done", Options()).ShouldBeFalse();
    }

    [Fact]
    public void TheDefaultReturnUrlHost_CountsAsAllowed()
    {
        var options = Options("https://shop.example.com/checkout/done");

        PaymentRedirectPolicy.IsAllowed("https://shop.example.com/orders/1", options).ShouldBeTrue();
        PaymentRedirectPolicy.IsAllowed("https://evil.example.net/orders/1", options).ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://shop.example.com/done", true)]
    [InlineData("https://SHOP.EXAMPLE.COM/done", true)]
    [InlineData("http://shop.example.com/done", true)]
    [InlineData("https://evil.com/done", false)]
    // 子域不算：一个被接管的子域（过期 CNAME 是常见来源）不该顺带进来
    [InlineData("https://pay.shop.example.com/done", false)]
    // 后缀相同不算：攻击者注册 shop.example.com.evil.com 就能通过前缀/后缀匹配
    [InlineData("https://shop.example.com.evil.com/done", false)]
    // 用户信息段不算：https://shop.example.com@evil.com 的真实主机是 evil.com
    [InlineData("https://shop.example.com@evil.com/done", false)]
    public void HostIsMatchedExactly(string url, bool expected)
    {
        PaymentRedirectPolicy.IsAllowed(url, Options(null, "shop.example.com")).ShouldBe(expected);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("/orders/1")]
    [InlineData("not a url")]
    public void OnlyAbsoluteHttpUrlsAreAccepted(string url)
    {
        // 相对地址在渠道那一侧没有意义；javascript: / data: 在浏览器里就是脚本执行
        PaymentRedirectPolicy.IsAllowed(url, Options(null, "shop.example.com")).ShouldBeFalse();
    }

    [Fact]
    public void ConfiguredHostsAndTheDefaultReturnUrlHost_AreBothAllowed()
    {
        var options = Options("https://shop.example.com/done", "app.example.com", "  m.example.com  ");

        PaymentRedirectPolicy.AllowedHosts(options)
            .OrderBy(h => h, StringComparer.Ordinal)
            .ShouldBe(["app.example.com", "m.example.com", "shop.example.com"]);
    }
}
