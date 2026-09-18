using Microsoft.Extensions.Configuration;
using Tnzi.Exceptions;
using Tnzi.Modules;
using Tnzi.Notification.Extensions;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// <see cref="NotificationModule"/> 按配置给每条渠道登记<b>默认 + 具名</b>发送器，
/// <see cref="INotificationProviderResolver"/> 按键把它们取出来。
/// </summary>
/// <remarks>
/// ★ 全部走真实的模块注册与真实的容器：keyed service 的登记与解析只体现在注册工厂那几行里，
/// 接错了编译照过 —— 症状是带键的消息全部报「服务商未注册」，或更糟，取到了另一家。
/// </remarks>
public class NamedSenderWiringTests
{
    private static readonly Dictionary<string, string?> DefaultAndNamedMail = new()
    {
        ["Notification:MailSender:SmtpServer"] = "smtp.example.com",
        ["Notification:MailSender:FromEmail"] = "noreply@example.com",
        ["Notification:MailSender:EnableSsl"] = "false",
        ["Notification:MailSenders:Marketing:SmtpServer"] = "smtp.news.example.com",
        ["Notification:MailSenders:Marketing:FromEmail"] = "news@example.com",
        ["Notification:MailSenders:Marketing:EnableSsl"] = "false",
    };

    /// <summary>
    /// 解析器与默认选择器由模块自己注册：<c>NotificationService</c> 的构造函数要它们，
    /// 漏了这两行的症状是「加载了 Notification 的应用第一次解析 INotificationService 就在容器里炸」。
    /// </summary>
    [Fact]
    public void TheModule_RegistersTheResolverAndTheDefaultSelector()
    {
        using var provider = BuildProvider([]);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<INotificationProviderResolver>().ShouldBeOfType<NotificationProviderResolver>();
        scope.ServiceProvider.GetRequiredService<INotificationProviderSelector>().ShouldBeOfType<DefaultNotificationProviderSelector>();
    }

    // ── 邮件 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ANamedMailProfile_IsRegisteredAsAKeyedMailKitSender()
    {
        using var provider = BuildProvider(DefaultAndNamedMail);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredKeyedService<IEmailSender>("marketing").ShouldBeOfType<MailKitEmailSender>();
        scope.ServiceProvider.GetRequiredService<IEmailSender>().ShouldBeOfType<MailKitEmailSender>();
    }

    /// <summary>★ 配置里写 <c>Marketing</c>，登记出来的键是 <c>marketing</c>；请求里怎么写大小写都取得到。</summary>
    [Theory]
    [InlineData("marketing")]
    [InlineData("Marketing")]
    [InlineData("MARKETING")]
    public void TheResolver_FindsANamedProfileRegardlessOfCase(string requestedKey)
    {
        using var provider = BuildProvider(DefaultAndNamedMail);
        using var scope = provider.CreateScope();

        var resolver = scope.ServiceProvider.GetRequiredService<INotificationProviderResolver>();

        resolver.Resolve<IEmailSender>(requestedKey).ShouldBeOfType<MailKitEmailSender>();
        resolver.IsRegistered(NotificationType.Email, requestedKey).ShouldBeTrue();
    }

    /// <summary>默认键的三种写法取到的是同一个实例（scoped）。</summary>
    [Fact]
    public void TheResolver_ReturnsTheDefaultSenderForNullBlankAndDefault()
    {
        using var provider = BuildProvider(DefaultAndNamedMail);
        using var scope = provider.CreateScope();

        var resolver = scope.ServiceProvider.GetRequiredService<INotificationProviderResolver>();
        var expected = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        resolver.Resolve<IEmailSender>(null).ShouldBeSameAs(expected);
        resolver.Resolve<IEmailSender>("  ").ShouldBeSameAs(expected);
        resolver.Resolve<IEmailSender>("Default").ShouldBeSameAs(expected);
    }

    /// <summary>★ 取不到就是 null，绝不退回默认发送器。</summary>
    [Fact]
    public void TheResolver_ReturnsNullForAnUnknownKey_NotTheDefault()
    {
        using var provider = BuildProvider(DefaultAndNamedMail);
        using var scope = provider.CreateScope();

        var resolver = scope.ServiceProvider.GetRequiredService<INotificationProviderResolver>();

        resolver.Resolve<IEmailSender>("nope").ShouldBeNull();
        resolver.IsRegistered(NotificationType.Email, "nope").ShouldBeFalse();
    }

    /// <summary>
    /// ★ 只配了具名节、没配默认节：默认发送器<b>失败</b>，不是那个报成功的 <see cref="NullEmailSender"/>。
    /// 一个显然要发信的部署里，没带键的消息被静默吞掉毫无症状。
    /// </summary>
    [Fact]
    public async Task NamedMailProfilesWithoutADefault_MakeTheDefaultSenderFailLoudly()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Notification:MailSenders:marketing:SmtpServer"] = "smtp.news.example.com",
            ["Notification:MailSenders:marketing:FromEmail"] = "news@example.com",
            ["Notification:MailSenders:marketing:EnableSsl"] = "false",
        });
        using var scope = provider.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        sender.ShouldBeOfType<UnconfiguredEmailSender>();
        var result = await sender.SendToAsync("a@example.com", null, "s", "b");
        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("Notification:MailSender");
    }

    /// <summary>什么都没配：沿用开发期的 <see cref="NullEmailSender"/>，行为不变。</summary>
    [Fact]
    public void NoMailConfigurationAtAll_KeepsTheNullSender()
    {
        using var provider = BuildProvider([]);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEmailSender>().ShouldBeOfType<NullEmailSender>();
    }

    // ── 短信 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ANamedSmsProfile_IsRegisteredAsAKeyedHttpSender()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Notification:SmsSenders:otp:Provider"] = "twilio",
            ["Notification:SmsSenders:otp:TwilioAccountSid"] = "sid",
            ["Notification:SmsSenders:otp:TwilioAuthToken"] = "tok",
            ["Notification:SmsSenders:otp:TwilioFromPhoneNumber"] = "+15550001111",
        });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredKeyedService<ISmsSender>("otp").ShouldBeOfType<HttpSmsSender>();
        scope.ServiceProvider.GetRequiredService<ISmsSender>().ShouldBeOfType<UnconfiguredSmsSender>();
    }

    // ── 传真 ─────────────────────────────────────────────────────────────────

    /// <summary>具名传真节可以指定承载它的邮件发送器。</summary>
    [Fact]
    public void ANamedFaxProfile_RidesOnTheMailSenderItNames()
    {
        var settings = new Dictionary<string, string?>(DefaultAndNamedMail)
        {
            ["Notification:FaxSenders:legal:GatewayDomain"] = "fax.example.com",
            ["Notification:FaxSenders:legal:EmailProviderKey"] = "Marketing",
        };
        using var provider = BuildProvider(settings);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredKeyedService<IFaxSender>("legal").ShouldBeOfType<EmailToFaxSender>();
    }

    /// <summary>
    /// ★ 指向一个不存在的邮件发送器：解析当场抛，<b>不退回默认邮件发送器</b> ——
    /// 退回去的信网关不认，症状是「传真发成功了但永远没到」。
    /// </summary>
    [Fact]
    public void AFaxProfileNamingAnUnknownMailSender_ThrowsInsteadOfFallingBack()
    {
        var settings = new Dictionary<string, string?>(DefaultAndNamedMail)
        {
            ["Notification:FaxSender:GatewayDomain"] = "fax.example.com",
            ["Notification:FaxSender:EmailProviderKey"] = "postmark",
        };
        using var provider = BuildProvider(settings);
        using var scope = provider.CreateScope();

        var exception = Should.Throw<ConfigurationException>(() => scope.ServiceProvider.GetRequiredService<IFaxSender>());

        exception.Message.ShouldContain("postmark");
        exception.ConfigurationKey.ShouldBe("Notification:FaxSender:EmailProviderKey");
    }

    // ── 推送（父模块侧）──────────────────────────────────────────────────────

    /// <summary>具名推送节配了却没加载实现：每个键都失败并指名要加载什么，与默认节同一分档。</summary>
    [Fact]
    public async Task ANamedPushProfileWithoutTheSubModule_FailsAndNamesTheModule()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Notification:PushSenders:ops:Provider"] = "fcm",
            ["Notification:PushSenders:ops:FirebaseProjectId"] = "p",
            ["Notification:PushSenders:ops:FirebaseServiceAccountJson"] = "{}",
        }, runPostConfigure: true);
        using var scope = provider.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredKeyedService<IPushSender>("ops");

        sender.ShouldBeOfType<UnconfiguredPushSender>();
        var result = await sender.SendToAsync("token", "t", "b");
        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNull().ShouldContain("Tnzi.Notification.Push");
    }

    /// <summary>
    /// ★ 只配了具名推送节、没配默认节（且没加载子模块）：<b>默认</b>发送器也失败，不是那个报成功的
    /// <see cref="NullPushSender"/> —— 与邮件 / 短信同一分档：显然要推送的部署里，没带键的消息落进空实现毫无症状。
    /// </summary>
    [Fact]
    public async Task NamedPushProfilesWithoutADefault_MakeTheDefaultSenderFailLoudly()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Notification:PushSenders:ops:Provider"] = "fcm",
            ["Notification:PushSenders:ops:FirebaseProjectId"] = "p",
            ["Notification:PushSenders:ops:FirebaseServiceAccountJson"] = "{}",
        }, runPostConfigure: true);
        using var scope = provider.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<IPushSender>();

        sender.ShouldBeOfType<UnconfiguredPushSender>();
        var byToken = await sender.SendToAsync("token", "t", "b");
        byToken.Success.ShouldBeFalse();
        byToken.FailureReason.ShouldNotBeNull().ShouldContain("Notification:PushSender");
        var byTopic = await sender.SendToTopicAsync("news", "t", "b");
        byTopic.Success.ShouldBeFalse();
        byTopic.FailureReason.ShouldNotBeNull().ShouldContain("Notification:PushSender");
    }

    /// <summary>什么都没配：沿用开发期的 <see cref="NullPushSender"/>，行为不变。</summary>
    [Fact]
    public void NoPushConfigurationAtAll_KeepsTheNullSender()
    {
        using var provider = BuildProvider([], runPostConfigure: true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IPushSender>().ShouldBeOfType<NullPushSender>();
    }

    // ── 消费方代码注册 ───────────────────────────────────────────────────────

    /// <summary>消费方在代码里注册的具名发送器（SendGrid 这类）与配置节走同一个出口，键照样收口。</summary>
    [Fact]
    public void ASenderRegisteredInCode_IsResolvedByItsNormalisedKey()
    {
        var stub = new Mock<IEmailSender>().Object;
        using var provider = BuildProvider(DefaultAndNamedMail, services =>
            services.AddNotificationSender<IEmailSender>("SendGrid", _ => stub));
        using var scope = provider.CreateScope();

        var resolver = scope.ServiceProvider.GetRequiredService<INotificationProviderResolver>();

        resolver.Resolve<IEmailSender>("sendgrid").ShouldBeSameAs(stub);
        resolver.Resolve<IEmailSender>("SENDGRID").ShouldBeSameAs(stub);
    }

    /// <summary>代码注册与配置节同键时，后注册的（消费方）赢 —— 与默认发送器的覆盖规则一致。</summary>
    [Fact]
    public void ASenderRegisteredInCode_OverridesTheConfiguredProfileWithTheSameKey()
    {
        var stub = new Mock<IEmailSender>().Object;
        using var provider = BuildProvider(DefaultAndNamedMail, services =>
            services.AddNotificationSender<IEmailSender>("marketing", _ => stub));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<INotificationProviderResolver>()
            .Resolve<IEmailSender>("marketing").ShouldBeSameAs(stub);
    }

    /// <summary>默认发送器不走具名注册；形状不合法的键也不登记 —— 登记一个消息永远指不到的键与没登记同症状。</summary>
    [Theory]
    [InlineData("default")]
    [InlineData("has space")]
    public void RegisteringAnInvalidNamedKey_Throws(string key)
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentException>(() => services.AddNotificationSender<IEmailSender>(key, _ => new Mock<IEmailSender>().Object));
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private static ServiceProvider BuildProvider(
        Dictionary<string, string?> settings,
        Action<IServiceCollection>? consumerRegistrations = null,
        bool runPostConfigure = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var context = new ServiceConfigurationContext(services, configuration);

        var module = new NotificationModule();
        module.PreConfigureServicesAsync(context).GetAwaiter().GetResult();
        module.ConfigureServicesAsync(context).GetAwaiter().GetResult();

        // 消费方模块的 LoadOrder 在本模块之后：它的 Configure 跑在本模块之后、PostConfigure 之前。
        consumerRegistrations?.Invoke(services);

        if (runPostConfigure)
            module.PostConfigureServicesAsync(context).GetAwaiter().GetResult();

        return services.BuildServiceProvider();
    }
}
