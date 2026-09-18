using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Hosting.Events.Handlers;
using Tnzi.Identity.Events;
using Tnzi.Identity.Options;
using Tnzi.Identity.Services;
using Tnzi.Notification.Dtos;
using Tnzi.Notification.Services;
using Tnzi.Results;
using Tnzi.System.Options;
using Tnzi.System.Services;

namespace Tnzi.AspNetCore.Tests.Hosting;

/// <summary>
/// 密码重置邮件：重置链接拼不出来时会发生什么。
///
/// 与 <see cref="UserRegisteredEventHandlerTests"/> 守的是同一条线的兄弟：此前三条路径都走不通时
/// 处理器记一条 Warning、返回空串，然后**照常发信** —— 模板的按钮 href 与可复制链接都是空的，
/// 日志里是「Password reset email sent」，Identity 侧令牌已签发且计入频控，
/// 用户既进不去也没有任何东西再试一次。链接生成在发信之前，抛出去让总线重试不会重复发信。
/// </summary>
public class PasswordResetRequestedEventHandlerTests
{
    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private readonly List<CreateNotificationRequest> _sent = [];

    private INotificationService Notifications()
    {
        var mock = new Mock<INotificationService>();
        mock.Setup(s => s.CreateAndSendAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateNotificationRequest, CancellationToken>((request, _) => _sent.Add(request))
            .ReturnsAsync(Result<NotificationInfo>.Success(new NotificationInfo()));
        return mock.Object;
    }

    private static ISettingService SettingsWith(string? apiBaseUrl, string? frontendUrl)
    {
        var mock = new Mock<ISettingService>();
        mock.Setup(s => s.GetAppNameAsync()).ReturnsAsync(Result<string>.Success("Acme"));
        mock.Setup(s => s.GetApplicationOptions())
            .Returns(new ApplicationOptions { ApiBaseUrl = apiBaseUrl, FrontendUrl = frontendUrl });
        return mock.Object;
    }

    private static IResetPasswordUrlGenerator GeneratorReturning(string? url)
    {
        var mock = new Mock<IResetPasswordUrlGenerator>();
        mock.Setup(g => g.GenerateUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(url!);
        return mock.Object;
    }

    private PasswordResetRequestedEventHandler Build(
        ISettingService? settings = null,
        IResetPasswordUrlGenerator? generator = null,
        string? resetPasswordRoute = null,
        IConfiguration? configuration = null)
        => new(
            Notifications(),
            NullLogger<PasswordResetRequestedEventHandler>.Instance,
            settingService: settings,
            identityOptions: new StaticMonitor<IdentityOptions>(new IdentityOptions
            {
                Recovery = new RecoveryOptions { ResetPasswordRoute = resetPasswordRoute ?? string.Empty, ResetTokenExpirationMinutes = 30 },
            }),
            urlGenerator: generator,
            configuration: configuration);

    private static IConfiguration ConfigWith(string key, string value)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();

    /// <summary>
    /// 前端 origin 与 Identity 那几处走同一个解析器：只在 <c>IConfiguration</c> 里配了 <c>System:FrontendUrl</c>
    /// 而没有 <c>ISettingService</c>（不加载 Tnzi.System 的宿主）也拼得出前端链接。
    /// </summary>
    [Fact]
    public async Task WithSystemFrontendUrlInConfigurationOnly_TheFrontendLinkIsBuilt()
    {
        var handler = Build(
            resetPasswordRoute: "/reset-password",
            configuration: ConfigWith(FrontendUrlResolver.PrimaryKey, "https://app.example.com/"));

        await handler.HandleAsync(AnEvent());

        var sent = Assert.Single(_sent);
        Assert.StartsWith("https://app.example.com/reset-password?", (string)sent.TemplateVariables!["ResetUrl"]);
    }

    /// <summary>旧键 <c>App:FrontendUrl</c> 仍然有效（解析器记 Warning），不是静默失效。</summary>
    [Fact]
    public async Task WithTheLegacyAppFrontendUrlOnly_TheFrontendLinkIsStillBuilt()
    {
        var handler = Build(
            resetPasswordRoute: "/reset-password",
            configuration: ConfigWith(FrontendUrlResolver.LegacyKey, "https://legacy.example.com"));

        await handler.HandleAsync(AnEvent());

        var sent = Assert.Single(_sent);
        Assert.StartsWith("https://legacy.example.com/reset-password?", (string)sent.TemplateVariables!["ResetUrl"]);
    }

    /// <summary>
    /// 解析器的主键字面量与 <see cref="ApplicationOptions"/> 的真实 section 必须是同一个键：
    /// 前者是核心里的字符串（Identity 读不到那个类型），后者是设置中心与 Hosting 守卫消息的来源。
    /// 两者漂开时，「按守卫消息加配置」与「Identity 拼链接」会各读各的。
    /// </summary>
    [Fact]
    public void TheResolverPrimaryKey_IsTheApplicationOptionsFrontendUrlKey()
    {
        var section = ConfigSectionResolver.Resolve(typeof(ApplicationOptions));
        Assert.Equal(FrontendUrlResolver.PrimaryKey, $"{section}:{nameof(ApplicationOptions.FrontendUrl)}");
    }

    private static PasswordResetRequestedEvent AnEvent()
        => new() { UserId = Guid.NewGuid(), UserName = "alice", Email = "alice@example.com", ResetToken = "reset-token" };

    [Fact]
    public async Task WithNoSettingServiceAndNoGenerator_NoResetEmailGoesOut()
    {
        // Identity + Notification 而不加载 Tnzi.System 的宿主一定走到这里：ISettingService 缺席，
        // 两个 URL 都是空串。此前的结果是一封按钮 href 为空的邮件。
        var handler = Build();

        var ex = await Assert.ThrowsAsync<TnziException>(() => handler.HandleAsync(AnEvent()));

        Assert.Contains("IResetPasswordUrlGenerator", ex.Message);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task WithASettingServiceThatHasNoUrls_NoResetEmailGoesOut()
    {
        var handler = Build(settings: SettingsWith(apiBaseUrl: null, frontendUrl: null));

        var ex = await Assert.ThrowsAsync<TnziException>(() => handler.HandleAsync(AnEvent()));

        Assert.Empty(_sent);
        AssertNamesTheRealSettingKeys(ex);
    }

    /// <summary>
    /// 失败消息是运维唯一看得到的出路，它点名的键必须真的绑定到处理器读的那两个值。
    /// 两个 URL 来自 <see cref="ISettingService.GetApplicationOptions"/>，而 <see cref="ApplicationOptions"/>
    /// 的 section 是 <c>System</c>（不是 <c>Application</c>）—— 09-04 与 09-12 的两条守卫都写成了
    /// 一个不存在的键，照它加配置重启后处理器继续抛同一条消息。键从类型上派生，改了 section 这里跟着变。
    /// </summary>
    internal static void AssertNamesTheRealSettingKeys(TnziException ex)
    {
        var section = ConfigSectionResolver.Resolve(typeof(ApplicationOptions));
        Assert.Contains($"{section}:{nameof(ApplicationOptions.ApiBaseUrl)}", ex.Message);
        Assert.Contains($"{section}:{nameof(ApplicationOptions.FrontendUrl)}", ex.Message);
        Assert.DoesNotContain("Application:", ex.Message);
    }

    [Fact]
    public async Task WhenTheCustomGeneratorReturnsNothingAndThereIsNoFallback_NoResetEmailGoesOut()
    {
        var handler = Build(generator: GeneratorReturning(null));

        await Assert.ThrowsAsync<TnziException>(() => handler.HandleAsync(AnEvent()));

        Assert.Empty(_sent);
    }

    [Fact]
    public async Task TheFailureHappensBeforeAnythingIsSent()
    {
        // 抛出去意味着总线会重试；链接生成在发信之前，所以重试不会重复发信。
        var handler = Build();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Assert.ThrowsAsync<TnziException>(() => handler.HandleAsync(AnEvent()));
        }

        Assert.Empty(_sent);
    }

    [Fact]
    public async Task WithAnApiBaseUrl_TheEmailCarriesAUsableResetLink()
    {
        // 防锈：配置齐全的部署一行都不该受影响，否则上面那几条会因为「压根没发信」而一起变绿。
        var handler = Build(settings: SettingsWith(apiBaseUrl: "https://api.example.com/", frontendUrl: null));

        await handler.HandleAsync(AnEvent());

        var sent = Assert.Single(_sent);
        Assert.Equal("PasswordReset", sent.TemplateName);
        var resetUrl = Assert.IsType<string>(sent.TemplateVariables!["ResetUrl"]);
        Assert.StartsWith("https://api.example.com/auth/reset-password?", resetUrl);
        Assert.Contains("token=reset-token", resetUrl);
    }

    [Fact]
    public async Task WithAFrontendRoute_TheLinkPointsAtTheFrontend()
    {
        var handler = Build(
            settings: SettingsWith(apiBaseUrl: null, frontendUrl: "https://app.example.com"),
            resetPasswordRoute: "/reset-password");

        await handler.HandleAsync(AnEvent());

        var sent = Assert.Single(_sent);
        Assert.StartsWith("https://app.example.com/reset-password?", (string)sent.TemplateVariables!["ResetUrl"]);
    }

    [Fact]
    public async Task WhenTheCustomGeneratorAnswers_ItsLinkIsUsed()
    {
        var handler = Build(generator: GeneratorReturning("https://custom.example.com/r?t=1"));

        await handler.HandleAsync(AnEvent());

        var sent = Assert.Single(_sent);
        Assert.Equal("https://custom.example.com/r?t=1", sent.TemplateVariables!["ResetUrl"]);
    }

    [Fact]
    public async Task AUserWithoutAnEmail_IsStillSkipped()
    {
        var handler = Build();

        await handler.HandleAsync(new PasswordResetRequestedEvent { UserId = Guid.NewGuid(), UserName = "alice", Email = string.Empty });

        Assert.Empty(_sent);
    }
}
