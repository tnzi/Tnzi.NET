using Tnzi.Notification.Services.Internal;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// RFC 8058 退订信头的组装规则（纯函数层）。
/// </summary>
/// <remarks>
/// 接线是否真的发生由 <c>Integration/UnsubscribeHeaderSendPathTests</c> 覆盖 ——
/// 只测纯函数永远不能作为「机制已接上」的证据，这个模块在退订、渠道开关、每小时上限上
/// 已经各栽过一次。
/// </remarks>
public class UnsubscribeHeadersTests
{
    private const string Landing = "https://app.example.com/unsubscribe";
    private const string OneClick = "https://api.example.com/api/notifications/unsubscribe/one-click";

    // ── 什么时候不写 ─────────────────────────────────────────────────────────

    /// <summary>
    /// ★★ 事务性消息一律不带。带了，客户端会照样渲染那个按钮，于是收件人可以「退订」掉
    /// 自己的安全邮件，然后再也收不到验证码。
    /// </summary>
    [Fact]
    public void ATransactionalMessage_GetsNoUnsubscribeHeader()
    {
        UnsubscribeHeaders.Build(isTransactional: true, Landing, OneClick, "tok").ShouldBeNull();
    }

    /// <summary>没配落地页就什么都不写：一个看着能用、点下去到不了的退订链接比没有更糟。</summary>
    [Fact]
    public void WithoutALandingUrl_NoHeaderIsWritten()
    {
        UnsubscribeHeaders.Build(isTransactional: false, null, OneClick, "tok").ShouldBeNull();
        UnsubscribeHeaders.Build(isTransactional: false, "   ", OneClick, "tok").ShouldBeNull();
    }

    [Fact]
    public void WithoutAToken_NoHeaderIsWritten()
    {
        UnsubscribeHeaders.Build(isTransactional: false, Landing, OneClick, "").ShouldBeNull();
    }

    // ── 写出来是什么样 ───────────────────────────────────────────────────────

    [Fact]
    public void TheLandingUrlCarriesTheToken()
    {
        var headers = UnsubscribeHeaders.Build(false, Landing, oneClickEndpoint: null, "abc.def")!;

        headers[UnsubscribeHeaders.ListUnsubscribe]
            .ShouldBe("<https://app.example.com/unsubscribe?token=abc.def>");
    }

    /// <summary>落地页自带查询参数时用 &amp; 续接，而不是再写一个 ?。</summary>
    [Fact]
    public void AnExistingQueryString_IsExtendedNotBroken()
    {
        var headers = UnsubscribeHeaders.Build(false, Landing + "?lang=fr", null, "tok")!;

        headers[UnsubscribeHeaders.ListUnsubscribe].ShouldContain("?lang=fr&token=tok");
    }

    [Fact]
    public void TheTokenIsUrlEncoded()
    {
        var headers = UnsubscribeHeaders.Build(false, Landing, null, "a+b/c=d")!;

        headers[UnsubscribeHeaders.ListUnsubscribe].ShouldContain("token=a%2Bb%2Fc%3Dd");
    }

    /// <summary>
    /// ★ 一键 POST 的 URI 必须排在最前：客户端从左往右挑第一个能用的。
    /// </summary>
    [Fact]
    public void TheOneClickEndpoint_ComesFirst()
    {
        var headers = UnsubscribeHeaders.Build(false, Landing, OneClick, "tok")!;

        var value = headers[UnsubscribeHeaders.ListUnsubscribe];
        value.IndexOf("api.example.com", StringComparison.Ordinal)
            .ShouldBeLessThan(value.IndexOf("app.example.com", StringComparison.Ordinal));
    }

    [Fact]
    public void TheOneClickEndpoint_DeclaresThePostCapability()
    {
        var headers = UnsubscribeHeaders.Build(false, Landing, OneClick, "tok")!;

        headers[UnsubscribeHeaders.ListUnsubscribePost].ShouldBe("List-Unsubscribe=One-Click");
    }

    /// <summary>
    /// ★★ 没有 POST 端点就不许声明一键能力：声明了却没有可 POST 的地址，邮件服务商会 POST
    /// 到落地页那个 GET 端点上 —— 收件人点了「退订」而什么也没发生。
    /// </summary>
    [Fact]
    public void WithoutAOneClickEndpoint_ThePostCapabilityIsNotDeclared()
    {
        var headers = UnsubscribeHeaders.Build(false, Landing, oneClickEndpoint: null, "tok")!;

        headers.ContainsKey(UnsubscribeHeaders.ListUnsubscribePost).ShouldBeFalse();
    }
}

/// <summary>
/// 信头真的写进了 <c>MimeMessage</c>，而不只是被交给了发送器。
/// </summary>
/// <remarks>
/// ★ <c>UnsubscribeHeaderSendPathTests</c> 断言的是「发送器收到了这个字典」——
/// 它用的是 mock 发送器，所以 <c>MailKitEmailSender</c> 把字典写进信头的那一步
/// <b>完全没有被覆盖</b>：把 <c>AddHeaders</c> 整个删掉，那组测试照样全绿，而每一封信
/// 都不再带退订按钮。这里直接检查 MimeKit 组装出来的那封信。
/// </remarks>
public class MailKitHeaderWritingTests
{
    private readonly NotificationOptions _options = new()
    {
        MaxConcurrency = 4,
        MailSender = new MailSenderOptions
        {
            FromEmail = "noreply@example.com",
            FromName = "Test",
            SmtpServer = "smtp.example.com",
            SmtpPort = 587,
            Username = "u",
            Password = "p",
        },
    };

    [Fact]
    public void CustomHeaders_AreWrittenOntoTheMimeMessage()
    {
        var message = new MimeKit.MimeMessage();

        InvokeAddHeaders(message, new Dictionary<string, string>
        {
            [UnsubscribeHeaders.ListUnsubscribe] = "<https://app.example.com/unsubscribe?token=tok>",
            [UnsubscribeHeaders.ListUnsubscribePost] = UnsubscribeHeaders.OneClickValue,
        });

        message.Headers[UnsubscribeHeaders.ListUnsubscribe]
            .ShouldBe("<https://app.example.com/unsubscribe?token=tok>");
        message.Headers[UnsubscribeHeaders.ListUnsubscribePost].ShouldBe("List-Unsubscribe=One-Click");
    }

    /// <summary>
    /// ★ 一个写不进去的信头名<b>不该让整封信发不出去</b>：信头是附加信息，正文才是这封信本身。
    /// </summary>
    [Fact]
    public void AMalformedHeaderName_IsSkippedInsteadOfThrowing()
    {
        var message = new MimeKit.MimeMessage();

        Should.NotThrow(() => InvokeAddHeaders(message, new Dictionary<string, string>
        {
            ["Bad: Name"] = "x",
            ["With\nNewline"] = "x",
            [""] = "x",
            [UnsubscribeHeaders.ListUnsubscribe] = "<https://app.example.com/unsubscribe?token=tok>",
        }));

        // 合法的那个仍然写进去了。
        message.Headers[UnsubscribeHeaders.ListUnsubscribe].ShouldNotBeNull();
    }

    private void InvokeAddHeaders(MimeKit.MimeMessage message, IReadOnlyDictionary<string, string> headers)
        => new MailKitEmailSender(
                _options.MailSender!, new Mock<IHttpClientFactory>().Object, new Mock<ILogger<MailKitEmailSender>>().Object)
            .AddHeaders(message, headers);
}
