namespace Tnzi.Notification.Push.Tests;

/// <summary>
/// 按<b>主题</b>投递（<c>IPushSender.SendToTopicAsync</c>）在 FCM 实现上的行为。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>这里的每一条都必须在碰到 Firebase 之前就有结论。</b><c>FirebaseApp</c> 是进程级单例，
/// 一旦某条用例走到 <c>EnsureFirebaseInitialized</c>，它要么依机器上有没有 ADC 而变成
/// 一条时灵时不灵的用例，要么把 <c>_firebaseInitialized</c> 置上、连累同进程里其它用例。
/// 所以「主题名合法」这一面不去断言投递成功，而是配 <c>apns</c> provider ——
/// 校验在分派之前，能走到 apns 那条分支本身就证明主题名被接受了。
/// </para>
/// </remarks>
public class PushTopicDeliveryTests
{
    private static PushSender CreateSender(string provider = "fcm") => new(
        new NotificationOptions
        {
            PushSender = new PushSenderOptions
            {
                Provider = provider,
                FirebaseProjectId = "test_project_id",
            },
        },
        new Mock<ILogger<PushSender>>().Object);

    /// <summary>与 <c>SendToAsync</c> 同一句话：没有推送配置就没有投递。</summary>
    [Fact]
    public async Task SendToTopicAsync_WithoutOptions_Fails()
    {
        var sender = new PushSender(new NotificationOptions(), new Mock<ILogger<PushSender>>().Object);

        var result = await sender.SendToTopicAsync("news", "Title", "Body");

        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("not configured");
    }

    /// <summary>
    /// ★ 不合法的主题名要以一句<b>读得懂</b>的失败结束：带上被拒的值、说清什么才算合法。
    /// </summary>
    /// <remarks>
    /// SDK 自己也会拦，但它抛的是 <c>ArgumentException("Malformed topic name.")</c> ——
    /// 那五个字落进 <c>Recipient.FailureReason</c> 之后，运维手上再没有别的线索。
    /// 主题名通常是从配置拼出来的（<c>alerts-{region}</c>），被拒的到底是哪个值是关键信息。
    /// </remarks>
    [Theory]
    [InlineData("news alerts")]            // 空格
    [InlineData("news/alerts")]            // 斜杠（且不是 /topics/ 前缀）
    [InlineData("news:alerts")]            // 冒号
    [InlineData("公告")]                      // 非 ASCII
    [InlineData("news\n")]                 // 尾随换行，肉眼看不出
    [InlineData("/topics/")]                 // 只有前缀，没有名字
    public async Task SendToTopicAsync_WithMalformedTopic_FailsWithReadableReason(string topic)
    {
        var sender = CreateSender();

        var result = await sender.SendToTopicAsync(topic, "Title", "Body");

        result.Success.ShouldBeFalse();
        var reason = result.FailureReason.ShouldNotBeNull();
        reason.ShouldContain(topic.Trim().Length == 0 ? "required" : topic);
        reason.ShouldContain("[a-zA-Z0-9-_.~%]+");
    }

    /// <summary>空主题名单独说一句「必填」，而不是把空串塞进「'' 不是合法主题」。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendToTopicAsync_WithBlankTopic_SaysItIsRequired(string topic)
    {
        var sender = CreateSender();

        var result = await sender.SendToTopicAsync(topic, "Title", "Body");

        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("required");
    }

    /// <summary>
    /// ★ 合法主题名（含 FCM 文档里那种 <c>/topics/</c> 写法）必须<b>穿过</b>校验。
    /// </summary>
    /// <remarks>
    /// 用 <c>apns</c> provider 观察：校验在 provider 分派之前，能拿到 apns 那条分支的答复，
    /// 就说明主题名没被校验拦下。比「断言投递成功」可靠得多 —— 后者要真连 FCM。
    /// <para>
    /// 若这里改成只认裸名字、拒掉 <c>/topics/news</c>，框架就比底层 SDK 更严，
    /// 症状是「照着 Firebase 官方文档写反而不行」。
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("news")]
    [InlineData("/topics/news")]
    [InlineData("news-alerts_2026.v1~x%20")]
    public async Task SendToTopicAsync_WithValidTopic_PassesValidationAndReachesProviderDispatch(string topic)
    {
        var sender = CreateSender(provider: "apns");

        var result = await sender.SendToTopicAsync(topic, "Title", "Body");

        result.Success.ShouldBeFalse();
        var reason = result.FailureReason.ShouldNotBeNull();
        reason.ShouldNotContain("[a-zA-Z0-9-_.~%]+");    // 不是被字符集拦下的
        reason.ShouldContain("fcm");
    }

    /// <summary>
    /// ★ apns 上的主题广播要说「换 fcm」，不能沿用那句「装上 APNs SDK 再来」。
    /// </summary>
    /// <remarks>
    /// 装上 APNs SDK 也不会有主题广播：APNs 没有 FCM 这种客户端自助订阅的主题
    /// （它的 <c>apns-topic</c> 是 bundle id）。指向一条走不通的路比说不支持更费时间，
    /// 而正确答案恰好很短 —— 配 fcm，iOS 由 FCM 转投 APNs。
    /// </remarks>
    [Fact]
    public async Task SendToTopicAsync_WithApnsProvider_PointsAtFcmNotAtTheApnsStub()
    {
        var sender = CreateSender(provider: "apns");

        var result = await sender.SendToTopicAsync("news", "Title", "Body");

        result.Success.ShouldBeFalse();
        var reason = result.FailureReason.ShouldNotBeNull();
        reason.ShouldContain("fcm");
        reason.ShouldNotContain("install the APNs SDK");
    }

    /// <summary>认不出的 provider 与 <c>SendToAsync</c> 一样报出来，不静默。</summary>
    [Fact]
    public async Task SendToTopicAsync_WithUnknownProvider_NamesIt()
    {
        var sender = CreateSender(provider: "onesignal");

        var result = await sender.SendToTopicAsync("news", "Title", "Body");

        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("onesignal");
    }

    /// <summary>
    /// ★ 主题地址填进设备令牌的位置，要就地指向 <c>SendToTopicAsync</c>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是可预见的误用，因为通知管线只有一条推送路径：<c>RecipientChannelDispatcher</c>
    /// 对 <c>NotificationType.Push</c> 一律调 <c>SendToAsync</c>，而主题投递刻意不在那条
    /// 管线上（退订按地址、偏好与频次上限按人，主题三者都没有）。于是「把主题名填进
    /// <c>Recipient.Address</c> 群发一次」是个自然的尝试。
    /// </para>
    /// <para>
    /// 不拦的话，症状是 FCM 回一句「不是合法的注册令牌」—— 它指向令牌本身，
    /// 而真正的问题是调错了方法。断言里因此同时要求：说出被拒的值、点名正确的方法。
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("/topics/news")]
    [InlineData("/topics/alerts-2026")]
    public async Task SendToAsync_WithTopicAddress_PointsAtSendToTopicAsync(string address)
    {
        var sender = CreateSender();

        var result = await sender.SendToAsync(address, "Title", "Body");

        result.Success.ShouldBeFalse();
        var reason = result.FailureReason.ShouldNotBeNull();
        reason.ShouldContain(address);
        reason.ShouldContain("SendToTopicAsync");
    }

    /// <summary>
    /// ★ 守卫只认 <c>/topics/</c> 前缀，<b>不去猜裸名字</b>。
    /// </summary>
    /// <remarks>
    /// 裸主题名与一个短令牌在字面上无从区分。去猜它就会开始拒绝一些本该交给 FCM 判的值，
    /// 那比它要解决的问题更糟。这条用例锁住「守卫不扩张」：一个不带前缀的值必须继续
    /// 往下走到 provider 分派，而不是被守卫拦在门口。
    /// </remarks>
    [Fact]
    public async Task SendToAsync_WithBareTokenLikeValue_IsNotMistakenForATopic()
    {
        var sender = CreateSender(provider: "apns");

        var result = await sender.SendToAsync("news", "Title", "Body");

        result.Success.ShouldBeFalse();
        // 走到了 apns 存根 = 没被主题守卫拦下。
        result.FailureReason.ShouldNotBeNull().ShouldContain("install the APNs SDK");
    }

    /// <summary>既有契约不变：<c>SendToAsync</c> 的行为不受本次改动影响。</summary>
    [Fact]
    public async Task SendToAsync_StillFailsWithoutOptions()
    {
        var sender = new PushSender(new NotificationOptions(), new Mock<ILogger<PushSender>>().Object);

        var result = await sender.SendToAsync("device_token", "Title", "Body");

        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("not configured");
    }
}
