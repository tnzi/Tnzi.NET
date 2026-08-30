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
}
