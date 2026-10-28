using Tnzi.Notification.Controllers;

namespace Tnzi.Notification.Tests.Controllers;

/// <summary>
/// 退订落地页回显的掩码地址（<c>GET unsubscribe</c>）：邮箱只露首字符与域名。
/// </summary>
public class UnsubscribePreviewMaskTests
{
    private static string? Preview(string address)
    {
        var optOut = new Mock<INotificationOptOutService>();
        optOut.Setup(o => o.ResolveUnsubscribeToken("token"))
            .Returns(new UnsubscribeTokenPayload(address, NotificationType.Email, null));

        return new DefaultUnsubscribeController(optOut.Object).Preview("token").Data?.MaskedAddress;
    }

    [Fact]
    public void Preview_MasksAnEmailToItsFirstCharacterAndDomain()
    {
        Preview("alice@example.com").ShouldBe("a***@example.com");
    }

    /// <summary>
    /// 本地部分以 emoji 开头：首字符按码元取会露出半个代理项（页面上是一个替换字符），所以一个都不露。
    /// </summary>
    [Fact]
    public void Preview_EmailStartingWithAnEmoji_ShowsNoLoneSurrogate()
    {
        Preview("\U0001F600alice@example.com").ShouldBe("***@example.com");
    }
}
