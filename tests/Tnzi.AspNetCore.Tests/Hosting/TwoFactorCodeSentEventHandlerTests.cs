using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Hosting.Events.Handlers;
using Tnzi.Identity.Events;
using Tnzi.Notification.Dtos;
using Tnzi.Notification.Services;
using Tnzi.Results;

namespace Tnzi.AspNetCore.Tests.Hosting;

/// <summary>
/// 验证码邮件 / 短信：没有验证码就没有可发的东西。
///
/// 此前处理器对 <c>TwoFactorCodeSentEvent.Code</c> 不做任何检查：事件里没带码
/// （消费应用自己发的事件、或经序列化边界丢掉了 <c>[JsonIgnore]</c> 的那个字段）
/// 就照发一封「Your verification code is 」，用户拿到一封没有码的邮件而日志记「sent」。
/// 与「拼不出链接就别发」是同一条线：发不出去就别发，比发一封残缺的重要。
/// </summary>
public class TwoFactorCodeSentEventHandlerTests
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

    private TwoFactorCodeSentEventHandler Build()
        => new(Notifications(), NullLogger<TwoFactorCodeSentEventHandler>.Instance);

    [Theory]
    [InlineData("Email", "alice@example.com")]
    [InlineData("Sms", "+15550001111")]
    public async Task WithoutACode_NothingIsSent(string type, string address)
    {
        var handler = Build();

        await Assert.ThrowsAsync<TnziException>(() => handler.HandleAsync(
            new TwoFactorCodeSentEvent { UserId = Guid.NewGuid(), UserName = "alice", Type = type, Address = address, Code = string.Empty, ExpirationMinutes = 5 }));

        Assert.Empty(_sent);
    }

    [Theory]
    [InlineData("Email", "alice@example.com", "Email")]
    [InlineData("Sms", "+15550001111", "Sms")]
    public async Task WithACode_TheCodeGoesOutOnTheRightChannel(string type, string address, string expectedChannel)
    {
        var handler = Build();

        await handler.HandleAsync(
            new TwoFactorCodeSentEvent { UserId = Guid.NewGuid(), UserName = "alice", Type = type, Address = address, Code = "123456", ExpirationMinutes = 5 });

        var sent = Assert.Single(_sent);
        Assert.Equal(expectedChannel, sent.Type.ToString());
        Assert.Equal("TwoFactorCode", sent.TemplateName);
        Assert.Equal("123456", sent.TemplateVariables!["Code"]);
    }
}
