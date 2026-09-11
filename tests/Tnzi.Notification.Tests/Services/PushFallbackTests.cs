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
        // 复刻 NotificationModule.PostConfigureServicesAsync 的选择逻辑。
        return options.PushSender != null
            ? new UnconfiguredPushSender(new Mock<ILogger<UnconfiguredPushSender>>().Object)
            : new NullPushSender(new Mock<ILogger<NullPushSender>>().Object);
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

    /// <summary>
    /// 一个「拆分前就存在」形态的自定义发送器：只实现了 <see cref="IPushSender.SendToAsync"/>。
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
