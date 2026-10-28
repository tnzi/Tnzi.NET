namespace Tnzi.Notification.Push.Tests;

/// <summary>
/// 带点击目标与数据的推送（<see cref="PushContent"/>）在 FCM 实现上的行为。
/// </summary>
/// <remarks>
/// <para>
/// ★ 与 <see cref="PushTopicDeliveryTests"/> 同一条纪律：<b>每一条都必须在碰到 Firebase 之前就有结论。</b>
/// 消息长什么样直接断言 <see cref="FcmPushPayload"/> 造出来的 <c>Message</c>；「内容合法」那一面配 <c>apns</c>
/// provider 观察：校验在分派之前，能走到 apns 那条分支就说明内容被接受了。
/// </para>
/// </remarks>
public class PushContentDeliveryTests
{
    private const string Token = "cX9vQm2Ls0aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789abcdefTAILPART";

    private static PushSender CreateSender(string provider = "fcm") => new(
        new PushSenderOptions
        {
            Provider = provider,
            FirebaseProjectId = "test_project_id",
        },
        new Mock<ILogger<PushSender>>().Object);

    private static PushContent WithLink(string link) => new("Title", "Body") { Link = link };

    private static PushContent WithDataKey(string key) => new("Title", "Body")
    {
        Data = new Dictionary<string, string> { [key] = "v" },
    };

    // ── 消息形状 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ★ 点击目标进 <c>webpush.fcm_options.link</c>（web 客户端点开时打开它），数据进顶层 <c>data</c>（所有平台都收得到）。
    /// </summary>
    [Fact]
    public void ForDevice_PutsTheLinkIntoWebpushAndTheDataAtTheTopLevel()
    {
        var content = new PushContent("Task finished", "Your report is ready")
        {
            Link = "https://app.example.com/chat/42",
            Data = new Dictionary<string, string> { ["threadId"] = "42", ["kind"] = "agent-run" },
        };

        var message = FcmPushPayload.ForDevice(Token, content);

#pragma warning disable CS0618 // Message.Token 是注册令牌的线缆字段，见 FcmPushPayload.ForDevice
        message.Token.ShouldBe(Token);
#pragma warning restore CS0618
        message.Topic.ShouldBeNull();
        message.Notification.ShouldNotBeNull().Title.ShouldBe("Task finished");
        message.Notification.Body.ShouldBe("Your report is ready");
        message.Webpush.ShouldNotBeNull().FcmOptions.ShouldNotBeNull().Link.ShouldBe("https://app.example.com/chat/42");
        message.Data.ShouldNotBeNull().Count.ShouldBe(2);
        message.Data["threadId"].ShouldBe("42");
        message.Data["kind"].ShouldBe("agent-run");
        // 链接不被顺手抄进数据：那要占一个框架保留的键，会与消费方自己的键相撞。
        message.Data.Values.ShouldNotContain("https://app.example.com/chat/42");
    }

    /// <summary>
    /// ★ 只带标题与正文的内容，消息与三参数发法从前造出来的逐字相同：不设 <c>Webpush</c>、不设 <c>Data</c>。
    /// </summary>
    /// <remarks>既有调用全部经这条路径发出，任何多出来的字段都是一次没人要过的线缆变化。</remarks>
    [Fact]
    public void ForDevice_WithTitleAndBodyOnly_ProducesTheSameMessageAsBefore()
    {
        var message = FcmPushPayload.ForDevice(Token, new PushContent("Title", "Body") { Link = " ", Data = new Dictionary<string, string>() });

        message.Notification.ShouldNotBeNull().Title.ShouldBe("Title");
        message.Notification.Body.ShouldBe("Body");
        message.Webpush.ShouldBeNull();
        message.Data.ShouldBeNull();
        message.Android.ShouldBeNull();
        message.Apns.ShouldBeNull();
    }

    /// <summary>主题广播同样带上链接与数据，寻址字段只设 <c>Topic</c>。</summary>
    [Fact]
    public void ForTopic_CarriesTheSameLinkAndDataAndNoToken()
    {
        var content = new PushContent("Title", "Body")
        {
            Link = "https://app.example.com/news/7",
            Data = new Dictionary<string, string> { ["articleId"] = "7" },
        };

        var message = FcmPushPayload.ForTopic("news", content);

        message.Topic.ShouldBe("news");
#pragma warning disable CS0618
        message.Token.ShouldBeNull();
#pragma warning restore CS0618
        message.Webpush.ShouldNotBeNull().FcmOptions.ShouldNotBeNull().Link.ShouldBe("https://app.example.com/news/7");
        message.Data.ShouldNotBeNull()["articleId"].ShouldBe("7");
    }

    // ── 链接校验 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ★ 链接不是绝对的 https 地址：以一句<b>带上被拒值</b>的失败结束，不交给 SDK 去抛。
    /// </summary>
    /// <remarks>
    /// <c>FirebaseAdmin</c> 自己也拦，但它抛的是 <c>ArgumentException("The link options should be a valid https url.")</c>
    /// 这句话不说被拒的是哪个值。链接通常是从路由与业务 id 拼出来的，基地址漏配时拼出来的正是 <c>/chat/42</c> 这种相对路径。
    /// 大写的 <c>HTTPS://</c> 也拒：SDK 按小写前缀判，放行它就等于把那句不带值的异常放回来。
    /// </remarks>
    [Theory]
    [InlineData("/chat/42")]                         // 相对路径（基地址漏配）
    [InlineData("chat/42")]
    [InlineData("http://app.example.com/chat/42")]   // 不是 https
    [InlineData("HTTPS://APP.EXAMPLE.COM/chat/42")]  // SDK 按小写前缀判
    [InlineData(" https://app.example.com/chat/42")] // 首部空白，肉眼看不出
    [InlineData("https://app.example.com/a b")]      // 未转义的空格
    [InlineData("javascript:alert(1)")]
    [InlineData("https:app.example.com")]
    public async Task SendToAsync_WithALinkThatIsNotAnAbsoluteHttpsUrl_FailsWithReadableReason(string link)
    {
        var result = await CreateSender().SendToAsync(Token, WithLink(link));

        result.Success.ShouldBeFalse();
        var reason = result.FailureReason.ShouldNotBeNull();
        reason.ShouldContain($"'{link}'");
        reason.ShouldContain("https://");
    }

    /// <summary>合法链接穿过校验，走到 provider 分派（apns 存根的那句话）。</summary>
    [Theory]
    [InlineData("https://app.example.com/chat/42")]
    [InlineData("https://app.example.com/devices/a1b2?tab=commands#latest")]
    [InlineData("https://localhost:5173/x")]
    public async Task SendToAsync_WithAnAbsoluteHttpsLink_PassesValidation(string link)
    {
        var result = await CreateSender(provider: "apns").SendToAsync(Token, WithLink(link));

        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("install the APNs SDK");
    }

    // ── 数据键校验 ───────────────────────────────────────────────────────────

    /// <summary>
    /// ★ FCM 与 Android 客户端保留的键：当场失败并点名那个键。
    /// </summary>
    /// <remarks>
    /// <para><c>from</c> / <c>message_type</c> / <c>google.*</c> / <c>gcm.notification.*</c> 是 FCM 服务端拒收的，
    /// 不拦的话远端回的是一句与键无关的 <c>InvalidArgument</c>。</para>
    /// <para>★ <c>collapse_key</c> 与其余 <c>gcm.*</c> 服务端<b>收下</b>，而 Android 客户端在 App 读到之前把它们剔掉：
    /// 投递成功、App 拿不到那个值、两边都没有日志。所以这里比服务端严。</para>
    /// </remarks>
    [Theory]
    [InlineData("from")]
    [InlineData("message_type")]
    [InlineData("collapse_key")]
    [InlineData("google.sent_time")]
    [InlineData("google.c.custom")]
    [InlineData("gcm.notification.title")]
    [InlineData("gcm.n.e")]
    [InlineData("gcm.thread")]
    public async Task SendToAsync_WithAReservedDataKey_FailsAndNamesTheKey(string key)
    {
        var result = await CreateSender().SendToAsync(Token, WithDataKey(key));

        result.Success.ShouldBeFalse();
        var reason = result.FailureReason.ShouldNotBeNull();
        reason.ShouldContain($"'{key}'");
        reason.ShouldContain("reserved");
    }

    /// <summary>
    /// 只是长得像的键照常放行：规则按整键与带点的前缀比对，不按子串猜。
    /// </summary>
    /// <remarks>两边的 SDK 都按 ordinal 比，大写的 <c>From</c> 不是保留词。</remarks>
    [Theory]
    [InlineData("fromUserId")]
    [InlineData("From")]
    [InlineData("googleId")]
    [InlineData("gcmx")]
    [InlineData("messageType")]
    [InlineData("threadId")]
    public async Task SendToAsync_WithAKeyThatOnlyLooksReserved_PassesValidation(string key)
    {
        var result = await CreateSender(provider: "apns").SendToAsync(Token, WithDataKey(key));

        result.FailureReason.ShouldNotBeNull().ShouldContain("install the APNs SDK");
    }

    /// <summary>空白键与 null 值：可空注解挡不住运行期的它们（未开可空分析的调用方、反序列化出来的字典）。</summary>
    [Fact]
    public async Task SendToAsync_WithABlankKeyOrANullValue_FailsWithReadableReason()
    {
        var blankKey = new PushContent("Title", "Body") { Data = new Dictionary<string, string> { [" "] = "v" } };
        var nullValue = new PushContent("Title", "Body") { Data = new Dictionary<string, string> { ["threadId"] = null! } };

        (await CreateSender().SendToAsync(Token, blankKey)).FailureReason.ShouldNotBeNull().ShouldContain("must not be blank");

        var reason = (await CreateSender().SendToAsync(Token, nullValue)).FailureReason.ShouldNotBeNull();
        reason.ShouldContain("'threadId'");
        reason.ShouldContain("null");
    }

    // ── 主题路径 ─────────────────────────────────────────────────────────────

    /// <summary>主题广播对内容做同一套校验。</summary>
    [Fact]
    public async Task SendToTopicAsync_ValidatesTheContentToo()
    {
        var badLink = await CreateSender().SendToTopicAsync("news", WithLink("/news/7"));
        var badKey = await CreateSender().SendToTopicAsync("news", WithDataKey("from"));
        var fine = await CreateSender(provider: "apns").SendToTopicAsync("news", WithLink("https://app.example.com/news/7"));

        badLink.FailureReason.ShouldNotBeNull().ShouldContain("'/news/7'");
        badKey.FailureReason.ShouldNotBeNull().ShouldContain("'from'");
        fine.FailureReason.ShouldNotBeNull().ShouldContain("fcm");   // 走到了 apns 主题分支
    }

    /// <summary>主题名先于内容判：两样都错时，先报主题名。</summary>
    [Fact]
    public async Task SendToTopicAsync_ReportsAMalformedTopicBeforeTheContent()
    {
        var result = await CreateSender().SendToTopicAsync("news alerts", WithLink("/news/7"));

        result.FailureReason.ShouldNotBeNull().ShouldContain("'news alerts'");
        result.FailureReason.ShouldNotContain("/news/7");
    }

    /// <summary>主题地址填进设备令牌的位置：仍先指向 <c>SendToTopicAsync</c>，再谈内容。</summary>
    [Fact]
    public async Task SendToAsync_ReportsATopicAddressBeforeTheContent()
    {
        var result = await CreateSender().SendToAsync("/topics/news", WithLink("/news/7"));

        result.FailureReason.ShouldNotBeNull().ShouldContain("SendToTopicAsync");
    }

    // ── 两种发法是同一条路径 ──────────────────────────────────────────────────

    /// <summary>
    /// ★ 三参数发法与只带标题正文的 <see cref="PushContent"/> 在每一条够得着的分支上给出逐字相同的答案。
    /// </summary>
    /// <remarks>
    /// 三参数方法只是新重载的一个写法；死令牌退役与日志掩码都住在那一条路径上，两种发法不可能各走各的。
    /// 这里在不连 FCM 的分支上对账：认不出的 provider、apns 存根、主题地址守卫，以及主题那一对。
    /// </remarks>
    [Theory]
    [InlineData("apns", Token)]
    [InlineData("onesignal", Token)]
    [InlineData("fcm", "/topics/news")]
    public async Task TheThreeArgumentCall_AndTheContentCall_GiveTheSameAnswer(string provider, string address)
    {
        var sender = CreateSender(provider);

        var legacy = await sender.SendToAsync(address, "Title", "Body");
        var withContent = await sender.SendToAsync(address, new PushContent("Title", "Body"));
        var legacyTopic = await sender.SendToTopicAsync("news", "Title", "Body");
        var withContentTopic = await sender.SendToTopicAsync("news", new PushContent("Title", "Body"));

        withContent.Success.ShouldBe(legacy.Success);
        withContent.FailureReason.ShouldBe(legacy.FailureReason);
        withContentTopic.Success.ShouldBe(legacyTopic.Success);
        withContentTopic.FailureReason.ShouldBe(legacyTopic.FailureReason);
    }
}
