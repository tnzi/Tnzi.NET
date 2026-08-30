using Microsoft.Extensions.Configuration;
using Tnzi.Modules;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// <see cref="NotificationModule"/> 到底把哪一个 <see cref="IFaxSender"/> 交给消费方。
/// </summary>
/// <remarks>
/// ★ 传真的回退实现与另外三条渠道**语义相反**（未配置时失败，而不是像 <c>Null*Sender</c> 那样报成功），
/// 所以"选中了哪一个"这件事本身就是要守的行为。它只体现在注册工厂的那几行条件里，
/// 接错了编译照过 —— 症状要么是没配传真的部署把每一份传真记成已投递，
/// 要么是配好了的部署一发传真就失败。
/// </remarks>
public class FaxSenderWiringTests
{
    [Fact]
    public void WithAGatewayDomain_TheModuleWiresTheEmailToFaxSender()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Notification:FaxSender:GatewayDomain"] = "fax.example.com"
        });

        provider.GetRequiredService<IFaxSender>().ShouldBeOfType<EmailToFaxSender>();
    }

    /// <summary>没有 FaxSender 这一节 = 这个部署不发传真。</summary>
    [Fact]
    public void WithNoFaxSection_TheModuleWiresTheUnconfiguredSender()
    {
        using var provider = BuildProvider([]);

        provider.GetRequiredService<IFaxSender>().ShouldBeOfType<UnconfiguredFaxSender>();
    }

    /// <summary>配置留着但这个环境先别发。</summary>
    [Fact]
    public void WhenDisabled_TheModuleWiresTheUnconfiguredSender()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Notification:FaxSender:Enabled"] = "false",
            ["Notification:FaxSender:GatewayDomain"] = "fax.example.com"
        });

        provider.GetRequiredService<IFaxSender>().ShouldBeOfType<UnconfiguredFaxSender>();
    }

    /// <summary>
    /// ★ 启用了但网关域名是空的：**不退化成任何一个发送器，而是直接炸**。
    /// </summary>
    /// <remarks>
    /// 这一条本来写成了「回退到 <see cref="UnconfiguredFaxSender"/>」，跑起来才发现前提就是错的：
    /// <c>AddTnziOptions</c> 挂了 <c>ValidateOnStart</c>，而注册工厂第一件事就是读
    /// <c>IOptions&lt;NotificationOptions&gt;.Value</c> —— 校验在那一刻触发，异常先于任何回退发生。
    /// 结论比原来的假设更好：一个空网关域名会拼出 <c>9055551234@</c> 这种地址，
    /// 与其让它安静地回退，不如让部署根本起不来。这条测试因此改为钉住"会抛"。
    /// </remarks>
    [Fact]
    public void WhenEnabledWithABlankGatewayDomain_ResolvingTheFaxSenderThrows()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Notification:FaxSender:Enabled"] = "true",
            ["Notification:FaxSender:GatewayDomain"] = ""
        });

        var exception = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IFaxSender>());

        exception.Message.ShouldContain("FaxSender.GatewayDomain");
    }

    /// <summary>
    /// 同一条规则在校验器上的直接形态。与 <c>MailSender</c> 那一节缺 <c>SmtpServer</c> 的处理一致：
    /// 配了这一节就得配全，缺了是启动期错误而不是运行期降级。
    /// </summary>
    [Fact]
    public void WhenEnabledWithABlankGatewayDomain_TheValidatorRejectsIt()
    {
        var options = new NotificationOptions
        {
            FaxSender = new FaxSenderOptions { Enabled = true, GatewayDomain = "   " }
        };

        var result = new NotificationOptionsValidator().Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("FaxSender.GatewayDomain");
    }

    /// <summary>网关域名写成邮箱地址会拼出 <c>905…@fax@example.com</c>，同样在启动期拦掉。</summary>
    [Fact]
    public void AGatewayDomainThatIsAnEmailAddress_IsRejectedAtStartup()
    {
        var options = new NotificationOptions
        {
            FaxSender = new FaxSenderOptions { GatewayDomain = "fax@example.com" }
        };

        var result = new NotificationOptionsValidator().Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("bare domain");
    }

    /// <summary>关掉的那一节不参与校验：留一份半成品配置在 appsettings 里不该让应用起不来。</summary>
    [Fact]
    public void ADisabledFaxSection_IsNotValidated()
    {
        var options = new NotificationOptions
        {
            FaxSender = new FaxSenderOptions { Enabled = false, GatewayDomain = string.Empty }
        };

        new NotificationOptionsValidator().Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    /// <summary>消费应用先注册自己的实现即可整体覆盖（模块用 <c>TryAddScoped</c>）。</summary>
    [Fact]
    public void AConsumerRegisteredFaxSender_WinsOverTheModuleDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IFaxSender, StubFaxSender>();

        using var provider = Configure(services, new Dictionary<string, string?>
        {
            ["Notification:FaxSender:GatewayDomain"] = "fax.example.com"
        });

        provider.GetRequiredService<IFaxSender>().ShouldBeOfType<StubFaxSender>();
    }

    #region 回执收件箱：配了才起

    /// <summary>
    /// ★ 没配收件箱 = 这个部署不收回执，<b>后台轮询根本不注册</b>。
    /// </summary>
    /// <remarks>
    /// "可选"不该做成"起一个每 5 分钟醒来发现自己没事干的线程"。这条断言的是注册那一刻的判据
    /// （<see cref="FaxConfirmationOptions.IsUsable"/>），它与配置校验用的是同一个属性 ——
    /// 两处各写一遍是这类开关最典型的漂移点：校验说"配好了"而注册说"没配"，
    /// 症状是配置完全正确、日志干净、回执就是不进来。
    /// </remarks>
    [Fact]
    public void WithNoConfirmationMailbox_TheModuleDoesNotStartThePoller()
    {
        var services = ConfigureServices(new Dictionary<string, string?>
        {
            ["Notification:FaxSender:GatewayDomain"] = "fax.example.com"
        });

        HasConfirmationPoller(services).ShouldBeFalse();
    }

    [Fact]
    public void WithAConfirmationMailbox_TheModuleStartsThePoller()
    {
        var services = ConfigureServices(new Dictionary<string, string?>
        {
            ["Notification:FaxSender:GatewayDomain"] = "fax.example.com",
            ["Notification:FaxSender:Confirmation:Host"] = "imap.example.com",
            ["Notification:FaxSender:Confirmation:UserName"] = "fax@example.com",
            ["Notification:FaxSender:Confirmation:Password"] = "secret"
        });

        HasConfirmationPoller(services).ShouldBeTrue();
    }

    /// <summary>配置留着但这个环境先别收。</summary>
    [Fact]
    public void WhenTheConfirmationMailboxIsDisabled_TheModuleDoesNotStartThePoller()
    {
        var services = ConfigureServices(new Dictionary<string, string?>
        {
            ["Notification:FaxSender:GatewayDomain"] = "fax.example.com",
            ["Notification:FaxSender:Confirmation:Enabled"] = "false",
            ["Notification:FaxSender:Confirmation:Host"] = "imap.example.com",
            ["Notification:FaxSender:Confirmation:UserName"] = "fax@example.com",
            ["Notification:FaxSender:Confirmation:Password"] = "secret"
        });

        HasConfirmationPoller(services).ShouldBeFalse();
    }

    /// <summary>
    /// ★ 判读、落库这两件服务**无条件注册**：它们不会自己动，注册了也不会有任何后台活动，
    /// 而 webhook / 人工补录这些别的回执来源要用得上它们。
    /// </summary>
    [Fact]
    public void TheConfirmationServicesAreAvailableEvenWithoutAMailbox()
    {
        // 断言的是"注册表里有没有"，不是"这个精简容器能不能把它造出来"：
        // FaxConfirmationService 要仓储，而这组测试刻意只装模块自己那几行。
        var services = ConfigureServices(new Dictionary<string, string?>
        {
            ["Notification:FaxSender:GatewayDomain"] = "fax.example.com"
        });

        services.ShouldContain(d =>
            d.ServiceType == typeof(IFaxConfirmationParser)
            && d.ImplementationType == typeof(HeuristicFaxConfirmationParser));
        services.ShouldContain(d => d.ServiceType == typeof(IFaxConfirmationService));
        services.ShouldContain(d => d.ServiceType == typeof(IFaxConfirmationMailbox));
    }

    /// <summary>
    /// ★ 填了 <c>Host</c> 却漏了账号是笔误，不是"不想收回执" —— 它的症状是"配了却不生效"，
    /// 没有报错、没有日志。所以在启动期拦掉。
    /// </summary>
    [Theory]
    [InlineData(null, "secret", "UserName")]
    [InlineData("fax@example.com", null, "Password")]
    public void AHalfConfiguredConfirmationMailbox_IsRejectedAtStartup(string? userName, string? password, string expected)
    {
        var options = new NotificationOptions
        {
            FaxSender = new FaxSenderOptions
            {
                GatewayDomain = "fax.example.com",
                Confirmation = new FaxConfirmationOptions
                {
                    Host = "imap.example.com",
                    UserName = userName,
                    Password = password
                }
            }
        };

        var result = new NotificationOptionsValidator().Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(expected);
    }

    /// <summary>整节缺省是合法的：回执是附加能力，没有它发传真一切照旧。</summary>
    [Fact]
    public void NoConfirmationSection_IsValid()
    {
        var options = new NotificationOptions
        {
            FaxSender = new FaxSenderOptions { GatewayDomain = "fax.example.com" }
        };

        new NotificationOptionsValidator().Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    /// <summary>秒级轮询只是白白敲人家的 IMAP，还可能被限流。</summary>
    [Fact]
    public void APollIntervalBelowTheFloor_IsRejected()
    {
        var options = new NotificationOptions
        {
            FaxSender = new FaxSenderOptions
            {
                GatewayDomain = "fax.example.com",
                Confirmation = new FaxConfirmationOptions
                {
                    Host = "imap.example.com",
                    UserName = "fax@example.com",
                    Password = "secret",
                    PollIntervalSeconds = 5
                }
            }
        };

        var result = new NotificationOptionsValidator().Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("PollIntervalSeconds");
    }

    private static bool HasConfirmationPoller(IServiceCollection services)
        => services.Any(d => d.ImplementationType == typeof(FaxConfirmationBackgroundService));

    #endregion

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return Configure(services, settings);
    }

    /// <summary>只跑模块的注册，把 <see cref="ServiceCollection"/> 原样交出来供检查。</summary>
    private static ServiceCollection ConfigureServices(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var context = new ServiceConfigurationContext(services, configuration);

        var module = new NotificationModule();
        module.PreConfigureServicesAsync(context).GetAwaiter().GetResult();
        module.ConfigureServicesAsync(context).GetAwaiter().GetResult();

        return services;
    }

    private static ServiceProvider Configure(ServiceCollection services, Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var context = new ServiceConfigurationContext(services, configuration);

        var module = new NotificationModule();
        module.PreConfigureServicesAsync(context).GetAwaiter().GetResult();
        module.ConfigureServicesAsync(context).GetAwaiter().GetResult();

        return services.BuildServiceProvider();
    }

    private sealed class StubFaxSender : IFaxSender
    {
        public Task<SendResult> SendToAsync(string faxNumber, EmailAttachment document, string? subject = null, CancellationToken cancellationToken = default)
            => Task.FromResult(SendResult.CreateSuccess("stub"));
    }
}
