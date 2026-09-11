using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Hosting.Events.Handlers;
using Tnzi.Identity.Events;
using Tnzi.Identity.Options;
using Tnzi.Identity.Services;
using Tnzi.Notification.Dtos;
using Tnzi.Notification.Services;
using Tnzi.Results;

namespace Tnzi.AspNetCore.Tests.Hosting;

/// <summary>
/// 注册后的欢迎邮件：确认链接生成不出来时会发生什么。
///
/// 守的是一条会把用户永久挡在门外的线。模板判的是
/// `RequireEmailConfirmation &amp;&amp; !string.IsNullOrEmpty(ConfirmationUrl)`，
/// 所以一个空的链接会让邮件**安静地**换成「不需要确认」那一版 ——
/// 在 `RequireConfirmedEmail=true` 的部署里，这个用户登不进去、收不到确认链接，
/// 也不会有任何东西再试一次。日志里只有一条 Warning，而邮件发送成功。
///
/// 事件处理器的铁律本就是「不吞异常」：总线自带错误隔离、重试与死信。
/// 同目录的 UserInvitedEventHandler 注释里写的正是这条，只是这个处理器没照做。
/// </summary>
public class UserRegisteredEventHandlerTests
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

    private static IRegistrationService RegistrationServiceReturning(Result<string> token)
    {
        var mock = new Mock<IRegistrationService>();
        mock.Setup(s => s.GenerateEmailConfirmationTokenAsync(It.IsAny<Guid>())).ReturnsAsync(token);
        return mock.Object;
    }

    private static IdentityOptions RequiringConfirmation(bool required)
        => new() { Registration = new RegistrationOptions { RequireConfirmedEmail = required } };

    private UserRegisteredEventHandler Build(IdentityOptions identityOptions, IRegistrationService? registrationService)
        => new(
            Notifications(),
            NullLogger<UserRegisteredEventHandler>.Instance,
            settingService: null,
            identityOptions: new StaticMonitor<IdentityOptions>(identityOptions),
            registrationService: registrationService);

    private static UserRegisteredEvent AnEvent()
        => new() { UserId = Guid.NewGuid(), UserName = "alice", Email = "alice@example.com" };

    [Fact]
    public async Task WhenTheTokenCannotBeGenerated_NoWelcomeEmailGoesOut()
    {
        // ★ 这是整条修复：宁可让总线重试 / 进死信，也不能发出一封
        //   看起来正常、却把用户永久挡在门外的邮件。
        var handler = Build(
            RequiringConfirmation(true),
            RegistrationServiceReturning(Result<string>.Failure("token store unavailable")));

        await Assert.ThrowsAsync<TnziException>(() => handler.HandleAsync(AnEvent()));

        Assert.Empty(_sent);
    }

    [Fact]
    public async Task WhenTheRegistrationServiceIsMissing_NoWelcomeEmailGoesOut()
    {
        var handler = Build(RequiringConfirmation(true), registrationService: null);

        await Assert.ThrowsAsync<TnziException>(() => handler.HandleAsync(AnEvent()));

        Assert.Empty(_sent);
    }

    [Fact]
    public async Task WhenNoBaseUrlIsConfigured_NoWelcomeEmailGoesOut()
    {
        // 拿得到令牌，却拼不出链接 —— 这一版此前同样是「发出去，链接留空」。
        var handler = Build(
            RequiringConfirmation(true),
            RegistrationServiceReturning(Result<string>.Success("confirm-token")));

        await Assert.ThrowsAsync<TnziException>(() => handler.HandleAsync(AnEvent()));

        Assert.Empty(_sent);
    }

    [Fact]
    public async Task TheFailureHappensBeforeAnythingIsSent()
    {
        // ★ 抛出去意味着总线会重试，所以「重试会不会重复发信」必须有个答案：
        //   不会 —— 链接生成在发信之前，失败时那封邮件根本没有被创建。
        var handler = Build(RequiringConfirmation(true), registrationService: null);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Assert.ThrowsAsync<TnziException>(() => handler.HandleAsync(AnEvent()));
        }

        Assert.Empty(_sent);
    }

    [Fact]
    public async Task WhenConfirmationIsNotRequired_TheEmailGoesOutAsBefore()
    {
        // 防锈：不需要确认的部署一行都不该受影响，
        // 否则上面那几条会因为「压根没发信」而一起变绿。
        var handler = Build(RequiringConfirmation(false), registrationService: null);

        await handler.HandleAsync(AnEvent());

        var sent = Assert.Single(_sent);
        Assert.Equal("WelcomeEmail", sent.TemplateName);
        Assert.Equal(false, sent.TemplateVariables!["RequireEmailConfirmation"]);
    }

    [Fact]
    public async Task AUserWithoutAnEmail_IsStillSkipped()
    {
        var handler = Build(RequiringConfirmation(true), registrationService: null);

        await handler.HandleAsync(new UserRegisteredEvent { UserId = Guid.NewGuid(), UserName = "alice" });

        Assert.Empty(_sent);
    }
}
