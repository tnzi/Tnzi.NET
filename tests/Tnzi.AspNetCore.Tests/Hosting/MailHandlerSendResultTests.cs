using Tnzi.Hosting.Events.Handlers;
using Tnzi.Identity.Events;
using Tnzi.Identity.Services;
using Tnzi.Notification.Dtos;
using Tnzi.Notification.Services;
using Tnzi.Results;

namespace Tnzi.AspNetCore.Tests.Hosting;

/// <summary>
/// 四个内置邮件处理器对 <c>INotificationService.CreateAndSendAsync</c> 返回的 <c>Result</c> 必须有反应。
///
/// 那个方法对业务失败从不抛异常：收件人校验不过、服务商键解析不出（09-11 起「只配具名节没配默认节」
/// 是一种真实配置）都在落库之前返回 Fail；SMTP 全部收件人失败时返回 Fail(500)。
/// 处理器此前把返回值丢掉、紧接着记一条 Information「... sent」——
/// 「不吞异常交总线重试」的承诺对 Result 形态的失败一个字都不成立：
/// 欢迎邮件、密码重置、验证码、邀请四类事务邮件在这些配置错误下静默丢失，日志里却是「sent」。
/// </summary>
public class MailHandlerSendResultTests
{
    private static readonly Result<NotificationInfo> Refused =
        Result<NotificationInfo>.Failure("Notification provider 'ses' is not registered", 500);

    private readonly List<CreateNotificationRequest> _sent = [];

    private INotificationService NotificationsReturning(Result<NotificationInfo> result)
    {
        var mock = new Mock<INotificationService>();
        mock.Setup(s => s.CreateAndSendAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateNotificationRequest, CancellationToken>((request, _) => _sent.Add(request))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static IResetPasswordUrlGenerator AGeneratorThatWorks()
    {
        var mock = new Mock<IResetPasswordUrlGenerator>();
        mock.Setup(g => g.GenerateUrl(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns("https://app.example.com/reset?token=t");
        return mock.Object;
    }

    // ---- UserRegistered ----

    [Fact]
    public async Task UserRegistered_WhenTheSendIsRefused_TheHandlerThrowsAndDoesNotLogSent()
    {
        var logger = new CapturingLogger<UserRegisteredEventHandler>();
        var handler = new UserRegisteredEventHandler(NotificationsReturning(Refused), logger);

        var ex = await Assert.ThrowsAsync<TnziException>(() => handler.HandleAsync(
            new UserRegisteredEvent { UserId = Guid.NewGuid(), UserName = "alice", Email = "alice@example.com" }));

        Assert.Contains("not registered", ex.Message);
        Assert.False(logger.LoggedInformationContaining("sent"));
    }

    [Fact]
    public async Task UserRegistered_WhenTheSendSucceeds_TheHandlerLogsSent()
    {
        // 防锈：成功路径要照旧记「sent」，否则上面那条会因为「永远不记」而绿。
        var logger = new CapturingLogger<UserRegisteredEventHandler>();
        var handler = new UserRegisteredEventHandler(
            NotificationsReturning(Result<NotificationInfo>.Success(new NotificationInfo())), logger);

        await handler.HandleAsync(new UserRegisteredEvent { UserId = Guid.NewGuid(), UserName = "alice", Email = "alice@example.com" });

        Assert.Single(_sent);
        Assert.True(logger.LoggedInformationContaining("sent"));
    }

    // ---- PasswordResetRequested ----

    [Fact]
    public async Task PasswordReset_WhenTheSendIsRefused_TheHandlerThrowsAndDoesNotLogSent()
    {
        var logger = new CapturingLogger<PasswordResetRequestedEventHandler>();
        var handler = new PasswordResetRequestedEventHandler(
            NotificationsReturning(Refused), logger, urlGenerator: AGeneratorThatWorks());

        await Assert.ThrowsAsync<TnziException>(() => handler.HandleAsync(
            new PasswordResetRequestedEvent { UserId = Guid.NewGuid(), UserName = "alice", Email = "alice@example.com", ResetToken = "t" }));

        Assert.False(logger.LoggedInformationContaining("sent"));
    }

    [Fact]
    public async Task PasswordReset_WhenTheSendSucceeds_TheHandlerLogsSent()
    {
        var logger = new CapturingLogger<PasswordResetRequestedEventHandler>();
        var handler = new PasswordResetRequestedEventHandler(
            NotificationsReturning(Result<NotificationInfo>.Success(new NotificationInfo())), logger, urlGenerator: AGeneratorThatWorks());

        await handler.HandleAsync(
            new PasswordResetRequestedEvent { UserId = Guid.NewGuid(), UserName = "alice", Email = "alice@example.com", ResetToken = "t" });

        Assert.Single(_sent);
        Assert.True(logger.LoggedInformationContaining("sent"));
    }

    // ---- TwoFactorCodeSent (email + sms) ----

    [Theory]
    [InlineData("Email", "alice@example.com")]
    [InlineData("Sms", "+15550001111")]
    public async Task TwoFactorCode_WhenTheSendIsRefused_TheHandlerThrowsAndDoesNotLogSent(string type, string address)
    {
        var logger = new CapturingLogger<TwoFactorCodeSentEventHandler>();
        var handler = new TwoFactorCodeSentEventHandler(NotificationsReturning(Refused), logger);

        await Assert.ThrowsAsync<TnziException>(() => handler.HandleAsync(
            new TwoFactorCodeSentEvent { UserId = Guid.NewGuid(), UserName = "alice", Type = type, Address = address, Code = "123456", ExpirationMinutes = 5 }));

        Assert.False(logger.LoggedInformationContaining("sent"));
    }

    [Theory]
    [InlineData("Email", "alice@example.com")]
    [InlineData("Sms", "+15550001111")]
    public async Task TwoFactorCode_WhenTheSendSucceeds_TheHandlerLogsSent(string type, string address)
    {
        var logger = new CapturingLogger<TwoFactorCodeSentEventHandler>();
        var handler = new TwoFactorCodeSentEventHandler(
            NotificationsReturning(Result<NotificationInfo>.Success(new NotificationInfo())), logger);

        await handler.HandleAsync(
            new TwoFactorCodeSentEvent { UserId = Guid.NewGuid(), UserName = "alice", Type = type, Address = address, Code = "123456", ExpirationMinutes = 5 });

        Assert.Single(_sent);
        Assert.True(logger.LoggedInformationContaining("sent"));
    }

    // ---- UserInvited ----

    [Fact]
    public async Task UserInvited_WhenTheSendIsRefused_TheHandlerThrowsAndDoesNotLogSent()
    {
        var logger = new CapturingLogger<UserInvitedEventHandler>();
        var handler = new UserInvitedEventHandler(NotificationsReturning(Refused), logger);

        await Assert.ThrowsAsync<TnziException>(() => handler.HandleAsync(AnInvitation()));

        Assert.False(logger.LoggedInformationContaining("sent"));
        Assert.False(logger.LoggedInformationContaining("resent"));
    }

    [Fact]
    public async Task UserInvited_WhenTheSendSucceeds_TheHandlerLogsSent()
    {
        var logger = new CapturingLogger<UserInvitedEventHandler>();
        var handler = new UserInvitedEventHandler(
            NotificationsReturning(Result<NotificationInfo>.Success(new NotificationInfo())), logger);

        await handler.HandleAsync(AnInvitation());

        var sent = Assert.Single(_sent);
        Assert.Equal("UserInvited", sent.TemplateName);
        Assert.True(logger.LoggedInformationContaining("sent"));
    }

    private static UserInvitedEvent AnInvitation() => new()
    {
        UserId = Guid.NewGuid(),
        UserName = "alice",
        Email = "alice@example.com",
        AcceptUrl = "https://app.example.com/invite/accept?token=t",
        ExpiresAt = DateTime.UtcNow.AddDays(7),
    };
}
