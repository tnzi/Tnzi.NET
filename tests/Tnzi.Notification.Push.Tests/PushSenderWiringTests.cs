using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Modules;
using Tnzi.Notification.Services;

namespace Tnzi.Notification.Push.Tests;

/// <summary>
/// 加载了 <see cref="NotificationPushModule"/> 时，父模块与本模块合起来给每个推送键交出哪一个 <see cref="IPushSender"/>。
/// </summary>
/// <remarks>
/// ★ 两个模块的注册按真实的生命周期次序跑（父 Pre/Configure → 子 Configure → 父 PostConfigure）：
/// 具名键的真实现由子模块在 Configure 阶段登记，父模块的 <c>UnconfiguredPushSender</c> 在 PostConfigure 用
/// <c>TryAdd</c> 补位 —— 次序反过来，每个具名键都会「配好了却一发就失败并让你去加载一个已经加载的包」。
/// </remarks>
public class PushSenderWiringTests
{
    private static readonly Dictionary<string, string?> DefaultAndNamed = new()
    {
        ["Notification:PushSender:Provider"] = "fcm",
        ["Notification:PushSender:FirebaseProjectId"] = "main-project",
        ["Notification:PushSender:FirebaseServiceAccountJson"] = "{}",
        ["Notification:PushSenders:Ops:Provider"] = "fcm",
        ["Notification:PushSenders:Ops:FirebaseProjectId"] = "ops-project",
        ["Notification:PushSenders:Ops:FirebaseServiceAccountJson"] = "{}",
    };

    /// <summary>具名推送节登记成真正的 <see cref="PushSender"/>，键按规范形态（小写）。</summary>
    [Fact]
    public void ANamedPushProfile_IsRegisteredAsARealPushSender()
    {
        using var provider = BuildProvider(DefaultAndNamed);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredKeyedService<IPushSender>("ops").ShouldBeOfType<PushSender>();
        scope.ServiceProvider.GetRequiredService<IPushSender>().ShouldBeOfType<PushSender>();
    }

    /// <summary>解析器按键取到的也是它，不是父模块那个补位的失败实现。</summary>
    [Fact]
    public void TheResolver_HandsOutTheRealSenderForANamedKey()
    {
        using var provider = BuildProvider(DefaultAndNamed);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<INotificationProviderResolver>()
            .Resolve<IPushSender>("OPS").ShouldBeOfType<PushSender>();
    }

    /// <summary>
    /// ★ 只配了具名节、没配默认节：<b>默认</b>发送器失败（与邮件 / 短信同一分档），具名键照样是真实现。
    /// 「没配默认节 = 这个部署不发推送」这条前提对只配了具名节的部署不成立 —— 它显然要推送，
    /// 没带键的消息落进一个报成功的空实现是路由错误不是开发模式。
    /// </summary>
    [Fact]
    public async Task NamedOnlyPushConfig_WithModule_DefaultSenderFails_NamedKeyStillReal()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Notification:PushSenders:ops:Provider"] = "fcm",
            ["Notification:PushSenders:ops:FirebaseProjectId"] = "ops-project",
            ["Notification:PushSenders:ops:FirebaseServiceAccountJson"] = "{}",
        });
        using var scope = provider.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<IPushSender>();
        sender.ShouldBeOfType<UnconfiguredPushSender>();
        var result = await sender.SendToAsync("token", "t", "b");
        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("Notification:PushSender");
        result.FailureReason.ShouldNotContain("Tnzi.Notification.Push", customMessage: "the package is loaded; the fix is configuration, not a package");

        scope.ServiceProvider.GetRequiredKeyedService<IPushSender>("ops").ShouldBeOfType<PushSender>();
    }

    /// <summary>什么都没配：仍是 <see cref="NullPushSender"/>（只用主题广播的部署也会加载本包，不能因此失败）。</summary>
    [Fact]
    public void NothingConfigured_StillNullPushSender()
    {
        using var provider = BuildProvider([]);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IPushSender>().ShouldBeOfType<NullPushSender>();
    }

    /// <summary>
    /// 具名发送器各用一个以键命名的 <c>FirebaseApp</c>；名字带前缀，不与宿主自己引导的实例撞名。
    /// </summary>
    [Fact]
    public void ANamedSender_UsesItsOwnFirebaseAppName()
    {
        PushSender.NamedAppName("ops").ShouldBe("tnzi:ops");
        PushSender.NamedAppName("ops").ShouldNotBe(PushSender.NamedAppName("marketing"));
    }

    /// <summary>
    /// ★ 两个不同的 Firebase 项目并存时，<c>SenderIdMismatch</c> 不退役令牌：注册表不记令牌属于哪个项目，
    /// 用项目 B 的发送器发给用户的全部令牌，项目 A 的令牌必然回它，而那一行是项目 A 推送的唯一地址。
    /// </summary>
    [Fact]
    public void TwoFirebaseProjects_SenderIdMismatchDoesNotRetireTheToken()
    {
        using var provider = BuildProvider(DefaultAndNamed);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredKeyedService<IPushSender>("ops").ShouldBeOfType<PushSender>()
            .SenderIdMismatchIsConclusive.ShouldBeFalse();
        scope.ServiceProvider.GetRequiredService<IPushSender>().ShouldBeOfType<PushSender>()
            .SenderIdMismatchIsConclusive.ShouldBeFalse();
    }

    /// <summary>只有一个项目（哪怕配了多个键指向它）时，<c>SenderIdMismatch</c> 照旧确证令牌已死。</summary>
    [Fact]
    public void OneFirebaseProject_UnderSeveralKeys_SenderIdMismatchStillRetires()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Notification:PushSender:FirebaseProjectId"] = "main-project",
            ["Notification:PushSender:FirebaseServiceAccountJson"] = "{}",
            ["Notification:PushSenders:ops:FirebaseProjectId"] = "MAIN-PROJECT",
            ["Notification:PushSenders:ops:FirebaseServiceAccountJson"] = "{}",
        });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredKeyedService<IPushSender>("ops").ShouldBeOfType<PushSender>()
            .SenderIdMismatchIsConclusive.ShouldBeTrue();
    }

    /// <summary>错误码判定：<c>Unregistered</c> 恒退役，<c>SenderIdMismatch</c> 看部署，<c>InvalidArgument</c> 恒不退役。</summary>
    [Theory]
    [InlineData(MessagingErrorCode.Unregistered, true, true)]
    [InlineData(MessagingErrorCode.Unregistered, false, true)]
    [InlineData(MessagingErrorCode.SenderIdMismatch, true, true)]
    [InlineData(MessagingErrorCode.SenderIdMismatch, false, false)]
    [InlineData(MessagingErrorCode.InvalidArgument, true, false)]
    public void TokenDeathVerdict(MessagingErrorCode code, bool senderIdMismatchIsConclusive, bool expected)
    {
        PushSender.IsTokenPermanentlyDead(code, senderIdMismatchIsConclusive).ShouldBe(expected);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var context = new ServiceConfigurationContext(services, configuration);

        var parent = new NotificationModule();
        var push = new NotificationPushModule();
        parent.PreConfigureServicesAsync(context).GetAwaiter().GetResult();
        parent.ConfigureServicesAsync(context).GetAwaiter().GetResult();
        push.ConfigureServicesAsync(context).GetAwaiter().GetResult();
        parent.PostConfigureServicesAsync(context).GetAwaiter().GetResult();

        // 设备注册表要真库；这里只看发送器的登记，用替身顶掉模块注册的那个（后注册的赢）。
        services.AddScoped(_ => new Mock<IPushDeviceService>().Object);

        return services.BuildServiceProvider();
    }
}
