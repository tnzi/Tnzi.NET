using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Hosting.Events.Handlers;
using Tnzi.Identity.Events;
using Tnzi.Notification.Dtos;
using Tnzi.Notification.Services;
using Tnzi.Results;

namespace Tnzi.AspNetCore.Tests.Hosting;

/// <summary>
/// 邀请邮件：接受链接不是一个能点开的地址时会发生什么。
///
/// 与 <see cref="PasswordResetRequestedEventHandlerTests"/> 守的是同一条线：「按钮点不开的邮件不能发」。
/// 默认的 <c>IInvitationUrlGenerator</c> 在 <c>Identity:Invitation:AcceptUrlTemplate</c> 与前端 origin
/// （<c>System:FrontendUrl</c>，旧键 <c>App:FrontendUrl</c>）都没配时返回 <c>/accept-invitation?token=...</c> 这样一条相对路径，此前处理器原样塞进模板照发：
/// 被邀请人收到一封排版漂亮的信，按钮与可复制链接都是相对路径，没有任何邮件客户端打得开；
/// 处理器记「sent」、服务层记「issued」，没有任何东西会再试一次。
/// 链接检查在发信之前，抛出去让总线重试不会重复发信。
/// </summary>
public class UserInvitedEventHandlerTests
{
    private readonly List<CreateNotificationRequest> _sent = [];

    private INotificationService Notifications()
    {
        var mock = new Mock<INotificationService>();
        mock.Setup(s => s.CreateAndSendAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateNotificationRequest, CancellationToken>((request, _) => _sent.Add(request))
            .ReturnsAsync(Result<NotificationInfo>.Success(new NotificationInfo()));
        return mock.Object;
    }

    private static UserInvitedEvent AnInvitationTo(string acceptUrl) => new()
    {
        UserId = Guid.NewGuid(),
        UserName = "alice",
        Email = "alice@example.com",
        AcceptUrl = acceptUrl,
        ExpiresAt = DateTime.UtcNow.AddDays(7),
    };

    [Theory]
    // 默认生成器在两个键都没配时给出的形状，逐字。
    [InlineData("/accept-invitation?token=abc")]
    [InlineData("accept-invitation?token=abc")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WithALinkNoMailClientCanOpen_NoInvitationEmailGoesOut(string acceptUrl)
    {
        var logger = new CapturingLogger<UserInvitedEventHandler>();
        var handler = new UserInvitedEventHandler(Notifications(), logger);

        var ex = await Assert.ThrowsAsync<TnziException>(() => handler.HandleAsync(AnInvitationTo(acceptUrl)));

        Assert.Empty(_sent);
        Assert.False(logger.LoggedInformationContaining("sent"));
        // 消息指名三条出路：模板键、前端地址键（主键在前，旧键也点名）、换掉生成器。
        Assert.Contains("Identity:Invitation:AcceptUrlTemplate", ex.Message);
        Assert.Contains("System:FrontendUrl", ex.Message);
        Assert.Contains("App:FrontendUrl", ex.Message);
        Assert.Contains("IInvitationUrlGenerator", ex.Message);
        // 令牌就在链接里，异常消息会进日志：链接本身不能出现在消息里。
        Assert.DoesNotContain("token=abc", ex.Message);
    }

    [Theory]
    [InlineData("https://app.example.com/accept-invitation?token=abc")]
    [InlineData("http://localhost:5173/accept-invitation?token=abc")]
    // 移动端深链是合法的绝对地址，消费方选它就该放行。
    [InlineData("acme-app://invitations/accept?token=abc")]
    public async Task WithAnAbsoluteLink_TheEmailCarriesIt(string acceptUrl)
    {
        var handler = new UserInvitedEventHandler(Notifications(), NullLogger<UserInvitedEventHandler>.Instance);

        await handler.HandleAsync(AnInvitationTo(acceptUrl));

        var sent = Assert.Single(_sent);
        Assert.Equal(acceptUrl, sent.TemplateVariables!["AcceptUrl"]);
    }

    [Fact]
    public async Task AnInvitationWithoutAnEmail_IsStillSkippedBeforeTheLinkIsChecked()
    {
        // 只有手机号的邀请本来就不走邮件；相对链接不该把「静默跳过」变成异常。
        var handler = new UserInvitedEventHandler(Notifications(), NullLogger<UserInvitedEventHandler>.Instance);
        var invitation = AnInvitationTo("/accept-invitation?token=abc");
        invitation.Email = null;

        await handler.HandleAsync(invitation);

        Assert.Empty(_sent);
    }
}
