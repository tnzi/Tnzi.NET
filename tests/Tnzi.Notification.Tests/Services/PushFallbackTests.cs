namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// 未加载 <c>Tnzi.Notification.Push</c> 时，父模块给出的两种回退。
/// </summary>
/// <remarks>
/// 分工按<b>意图</b>划，不按有没有实现划：没配推送 = 这个部署不发推送（沿用既有的
/// <see cref="NullPushSender"/>，行为与拆分前一致）；配了推送却没有实现 = 部署方要发推送
/// 但少装了包，必须当场失败并指名要加载什么。后者若报成功，症状要几周后才出现。
/// </remarks>
public class PushFallbackTests
{
    private static IPushSender Resolve(NotificationOptions options)
    {
        // 复刻 NotificationModule.PostConfigureServicesAsync 的三档选择逻辑；真实接线由 NamedSenderWiringTests 守着。
        var logger = new Mock<ILogger<UnconfiguredPushSender>>().Object;
        if (options.PushSender != null)
            return new UnconfiguredPushSender(logger);
        if (options.PushSenders.Count > 0)
            return UnconfiguredPushSender.ForMissingDefault(logger);
        return new NullPushSender(new Mock<ILogger<NullPushSender>>().Object);
    }

    /// <summary>没配推送：沿用旧行为，报成功。</summary>
    [Fact]
    public async Task NoPushConfigured_KeepsTheNullSenderBehaviour()
    {
        var sender = Resolve(new NotificationOptions());

        sender.ShouldBeOfType<NullPushSender>();
        var result = await sender.SendToAsync("token", "t", "b");
        result.Success.ShouldBeTrue();
    }

    /// <summary>★配了推送却没有实现：失败，且失败原因要指名该加载哪个模块。</summary>
    [Fact]
    public async Task PushConfiguredButNoImplementation_FailsAndNamesTheModule()
    {
        var sender = Resolve(new NotificationOptions
        {
            PushSender = new PushSenderOptions { Provider = "fcm", FirebaseProjectId = "p" },
        });

        sender.ShouldBeOfType<UnconfiguredPushSender>();
        var result = await sender.SendToAsync("token", "t", "b");
        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("Tnzi.Notification.Push");
    }

    /// <summary>
    /// 没配推送：<b>按主题</b>投递也报成功，与按设备令牌同一答案。
    /// </summary>
    /// <remarks>
    /// ★ 这条守的是「同一个部署不能对两种寻址方式给出相反的答案」。
    /// <c>IPushSender.SendToTopicAsync</c> 的默认接口实现是<b>失败</b>，
    /// <see cref="NullPushSender"/> 若不显式实现它就会继承那条失败 ——
    /// 于是开发期宿主里按令牌发一切正常、按主题发处处报错，而这个部署本来就不真发任何东西。
    /// 分档判据是<b>意图</b>，与寻址方式无关。
    /// </remarks>
    [Fact]
    public async Task NoPushConfigured_TopicDeliveryKeepsTheSameAnswerAsTokenDelivery()
    {
        var sender = Resolve(new NotificationOptions());

        sender.ShouldBeOfType<NullPushSender>();
        var result = await sender.SendToTopicAsync("news", "t", "b");
        result.Success.ShouldBeTrue();
    }

    /// <summary>
    /// ★配了推送却没有实现：<b>按主题</b>投递同样失败，且同样要指名该加载哪个模块。
    /// </summary>
    /// <remarks>
    /// 默认接口实现也会失败，但它说的是「这个实现不支持主题投递」，会把部署方引向
    /// 「换一个支持主题的实现」；真实原因是包没加载。两句话都失败，只有一句修得好。
    /// </remarks>
    [Fact]
    public async Task PushConfiguredButNoImplementation_TopicDeliveryAlsoNamesTheModule()
    {
        var sender = Resolve(new NotificationOptions
        {
            PushSender = new PushSenderOptions { Provider = "fcm", FirebaseProjectId = "p" },
        });

        sender.ShouldBeOfType<UnconfiguredPushSender>();
        var result = await sender.SendToTopicAsync("news", "t", "b");
        result.Success.ShouldBeFalse();
        var reason = result.FailureReason.ShouldNotBeNull();
        // ★ 断言必须能**区分**两条消息，否则它守不住它自己声称守的那件事。
        // 接口默认体里同样写着「or load the Tnzi.Notification.Push module.」，只查这个
        // 子串的话，把本类的显式重写整个删掉这条测试照样绿 —— 而那正是它要拦的回归。
        // 所以查的是只有本类才有的那一句，并显式排除默认体的措辞。
        reason.ShouldContain("no IPushSender implementation is registered");
        reason.ShouldContain("Tnzi.Notification.Push");
        reason.ShouldNotContain("does not support topic push delivery");
    }

    /// <summary>
    /// 只实现了 <c>SendToAsync</c> 的历史实现仍然满足接口，且主题投递<b>不会</b>被悄悄降级。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 两件事一起守：① 消费方自带的 <c>IPushSender</c> 不因新增成员而编译不过
    /// （<c>SendToTopicAsync</c> 是默认接口方法）；② 默认体<b>返回失败</b>而不是报成功 ——
    /// 一个报了成功却什么都没广播出去的实现毫无症状：日志干净、状态是 Sent、没有退信可查。
    /// </para>
    /// <para>
    /// 也不退化成「拿主题名当设备令牌发一条」：那会把一次面向全体订阅者的广播，
    /// 悄悄换成一次投递到一个不存在的设备。断言里因此要求默认体一次都没碰
    /// <c>SendToAsync</c>。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task LegacySenderWithoutTopicSupport_FailsInsteadOfSilentlySucceedingOrDegrading()
    {
        var probe = new TokenOnlyPushSender();
        // 默认接口成员只能经接口调用，而消费方从容器里拿到的正是 IPushSender。
        IPushSender sender = probe;

        var result = await sender.SendToTopicAsync("news", "t", "b");

        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("does not support topic push delivery");
        probe.TokenSendCount.ShouldBe(0);
    }

    // ── 带点击目标与数据的发法（PushContent）────────────────────────────────

    private static readonly PushContent ContentWithLinkAndData = new("t", "b")
    {
        Link = "https://app.example.com/chat/42",
        Data = new Dictionary<string, string> { ["threadId"] = "42" },
    };

    /// <summary>
    /// ★ 没配推送：带链接与数据的发法也报成功，与三参数那个同一答案。
    /// </summary>
    /// <remarks>
    /// 接口默认体对带链接或数据的内容<b>返回失败</b>。<see cref="NullPushSender"/> 若不显式实现新重载，
    /// 开发期宿主里同一条推送就会因为「多带了一个链接」从成功变成失败，而这个部署本来就不真发任何东西。
    /// </remarks>
    [Fact]
    public async Task NoPushConfigured_ContentWithLinkAndDataKeepsTheSameAnswer()
    {
        var sender = Resolve(new NotificationOptions());

        sender.ShouldBeOfType<NullPushSender>();
        (await sender.SendToAsync("token", ContentWithLinkAndData)).Success.ShouldBeTrue();
        (await sender.SendToTopicAsync("news", ContentWithLinkAndData)).Success.ShouldBeTrue();
    }

    /// <summary>
    /// ★ 配了推送却没有实现：带链接与数据的发法同样失败，且说的是<b>同一句</b>指名要加载哪个模块的话。
    /// </summary>
    /// <remarks>
    /// 断言排除接口默认体的措辞（「does not support push click targets」）：那句话也是失败，
    /// 但它把部署方引向「换一个支持点击目标的实现」，而真实原因是包没加载。删掉显式重写，这条会变红。
    /// </remarks>
    [Fact]
    public async Task PushConfiguredButNoImplementation_ContentWithLinkAndDataNamesTheModule()
    {
        var sender = Resolve(new NotificationOptions
        {
            PushSender = new PushSenderOptions { Provider = "fcm", FirebaseProjectId = "p" },
        });

        var byToken = await sender.SendToAsync("token", ContentWithLinkAndData);
        var byTopic = await sender.SendToTopicAsync("news", ContentWithLinkAndData);

        foreach (var result in new[] { byToken, byTopic })
        {
            result.Success.ShouldBeFalse();
            var reason = result.FailureReason.ShouldNotBeNull();
            reason.ShouldContain("no IPushSender implementation is registered");
            reason.ShouldNotContain("does not support push click targets");
        }
    }

    /// <summary>只配了具名节没配默认节：新发法与旧发法同一句指向配置的失败。</summary>
    [Fact]
    public async Task NamedOnlyPushConfig_ContentWithLinkAndDataPointsAtConfiguration()
    {
        var sender = Resolve(new NotificationOptions
        {
            PushSenders = new Dictionary<string, PushSenderOptions>
            {
                ["ops"] = new() { Provider = "fcm", FirebaseProjectId = "p" },
            },
        });

        var legacy = await sender.SendToAsync("token", "t", "b");
        var withContent = await sender.SendToAsync("token", ContentWithLinkAndData);

        withContent.Success.ShouldBeFalse();
        withContent.FailureReason.ShouldBe(legacy.FailureReason);
        withContent.FailureReason.ShouldNotBeNull().ShouldContain("Notification:PushSender");
    }

    /// <summary>
    /// 只实现了三参数方法的历史实现：只带标题与正文的 <see cref="PushContent"/> 照常转交过去发出去。
    /// </summary>
    [Fact]
    public async Task LegacySender_ContentWithTitleAndBodyOnly_IsDeliveredThroughTheThreeArgumentMethod()
    {
        var probe = new TokenOnlyPushSender();
        IPushSender sender = probe;

        var result = await sender.SendToAsync("token", new PushContent("t", "b") { Link = "  ", Data = new Dictionary<string, string>() });

        result.Success.ShouldBeTrue();
        result.ExternalMessageId.ShouldBe("legacy-id");
        probe.TokenSendCount.ShouldBe(1);
    }

    /// <summary>
    /// ★ 只实现了三参数方法的历史实现：带了点击目标或数据，默认体<b>失败</b>，不丢掉它们照发。
    /// </summary>
    /// <remarks>
    /// 丢掉照发的症状是：推送送达、结果成功，用户点开的是首页，原生客户端拿不到据以路由的键，
    /// 调用方明确要了的东西被安静地拿掉了。断言里因此同时要求三参数方法一次都没被调用。
    /// </remarks>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LegacySender_ContentWithLinkOrData_FailsInsteadOfDroppingThem(bool withLink, bool withData)
    {
        var probe = new TokenOnlyPushSender();
        IPushSender sender = probe;
        var content = new PushContent("t", "b")
        {
            Link = withLink ? "https://app.example.com/chat/42" : null,
            Data = withData ? new Dictionary<string, string> { ["threadId"] = "42" } : null,
        };

        var byToken = await sender.SendToAsync("token", content);
        var byTopic = await sender.SendToTopicAsync("news", content);

        foreach (var result in new[] { byToken, byTopic })
        {
            result.Success.ShouldBeFalse();
            var reason = result.FailureReason.ShouldNotBeNull();
            reason.ShouldContain(nameof(TokenOnlyPushSender));
            reason.ShouldContain("does not support push click targets or data");
        }
        probe.TokenSendCount.ShouldBe(0);
    }

    /// <summary>
    /// 只带标题与正文的主题广播交给三参数的主题方法：对一个不支持主题的历史实现，答案照旧是那句「不支持主题投递」。
    /// </summary>
    [Fact]
    public async Task LegacySender_TopicContentWithTitleAndBodyOnly_KeepsTheTopicAnswer()
    {
        var probe = new TokenOnlyPushSender();
        IPushSender sender = probe;

        var result = await sender.SendToTopicAsync("news", new PushContent("t", "b"));

        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("does not support topic push delivery");
        probe.TokenSendCount.ShouldBe(0);
    }

    /// <summary>
    /// <see cref="PushContent.Data"/> 是赋值那一刻的只读快照：调用方事后改自己的字典，交出去的内容不变。
    /// </summary>
    [Fact]
    public void PushContentData_IsAReadOnlySnapshot()
    {
        var source = new Dictionary<string, string> { ["threadId"] = "42" };
        var content = new PushContent("t", "b") { Data = source };

        source["threadId"] = "43";
        source["extra"] = "x";

        content.Data.ShouldNotBeNull().Count.ShouldBe(1);
        content.Data["threadId"].ShouldBe("42");
        Should.Throw<NotSupportedException>(() => ((IDictionary<string, string>)content.Data).Add("late", "y"));
    }

    /// <summary>空白链接与空字典都当「没有」：这样的内容与三参数发法等价。</summary>
    [Theory]
    [InlineData(null, false, false)]
    [InlineData("", false, false)]
    [InlineData("   ", true, false)]
    [InlineData("https://app.example.com", false, true)]
    [InlineData(null, true, false)]
    public void PushContent_CarriesLinkOrDataOnlyWhenThereIsSomething(string? link, bool emptyData, bool expected)
    {
        var content = new PushContent("t", "b")
        {
            Link = link,
            Data = emptyData ? new Dictionary<string, string>() : null,
        };

        content.HasLinkOrData.ShouldBe(expected);
        (content with { Data = new Dictionary<string, string> { ["k"] = "v" } }).HasLinkOrData.ShouldBeTrue();
    }

    /// <summary>
    /// 一个「拆分前就存在」形态的自定义发送器：只实现了 <see cref="IPushSender.SendToAsync(string, string, string, CancellationToken)"/>。
    /// </summary>
    private sealed class TokenOnlyPushSender : IPushSender
    {
        public int TokenSendCount { get; private set; }

        public Task<SendResult> SendToAsync(string deviceToken, string title, string body, CancellationToken cancellationToken = default)
        {
            TokenSendCount++;
            return Task.FromResult(SendResult.CreateSuccess("legacy-id"));
        }
    }
}
