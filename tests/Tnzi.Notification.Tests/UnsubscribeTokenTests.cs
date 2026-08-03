using Tnzi.Notification.Metadata;

namespace Tnzi.Notification.Tests;

/// <summary>
/// 一键退订令牌的签发与校验。
/// </summary>
/// <remarks>
/// 令牌是自包含且带签名的，不落库：退订链接的寿命等同于那封邮件在收件箱里的寿命（可能是几年），
/// 为此维护一张永不过期的令牌表是纯粹的负担。所以签名的正确性就是这条链路的全部安全性。
/// </remarks>
public class UnsubscribeTokenTests
{
    private static NotificationOptOutService CreateService(string? secret = "test-secret-value")
    {
        var options = new Mock<IOptionsMonitor<NotificationOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new NotificationOptions
        {
            OptOut = new OptOutOptions { TokenSecret = secret },
        });

        // 令牌的签发与校验是纯函数，不碰仓储 —— 传一个从不被调用的 mock。
        return new NotificationOptOutService(
            new Mock<IServiceProvider>().Object,
            new Mock<IRepository<OptOut, Guid>>().Object,
            options.Object);
    }

    [Fact]
    public void A_token_round_trips_its_payload()
    {
        var service = CreateService();

        var token = service.CreateUnsubscribeToken("Alice@Example.COM", NotificationType.Email, "marketing");
        var payload = service.ResolveUnsubscribeToken(token);

        payload.ShouldNotBeNull();
        // 地址归一化后存储：名单里的大小写与实际发送地址常常不一致，不归一化就会
        // 出现「退订了却还在收」这种最难查的失效。
        payload!.Address.ShouldBe("alice@example.com");
        payload.Channel.ShouldBe(NotificationType.Email);
        payload.Category.ShouldBe("marketing");
    }

    [Fact]
    public void A_whole_channel_token_carries_a_null_category()
    {
        var service = CreateService();

        var payload = service.ResolveUnsubscribeToken(
            service.CreateUnsubscribeToken("bob@example.com", NotificationType.Sms));

        payload.ShouldNotBeNull();
        payload!.Category.ShouldBeNull();
    }

    [Fact]
    public void A_token_signed_with_another_secret_is_rejected()
    {
        // ★ 这条是核心。签名失效不会有任何症状 —— 链接照常打开，直到有人发现
        //   自己被别人退订了。
        var token = CreateService("secret-one")
            .CreateUnsubscribeToken("alice@example.com", NotificationType.Email);

        CreateService("secret-two").ResolveUnsubscribeToken(token).ShouldBeNull();
    }

    [Fact]
    public void A_tampered_payload_is_rejected()
    {
        var service = CreateService();
        var token = service.CreateUnsubscribeToken("alice@example.com", NotificationType.Email);

        // 换掉载荷、保留原签名 —— 正是"替别人退订"会尝试的手法。
        var forgedPayload = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("victim@example.com|1|"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var forged = forgedPayload + token[token.IndexOf('.')..];

        service.ResolveUnsubscribeToken(forged).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-dot-at-all")]
    [InlineData(".")]
    [InlineData("payload.")]
    [InlineData("!!!not-base64!!!.!!!")]
    public void Malformed_tokens_return_null_rather_than_throwing(string token)
    {
        // 这个端点是匿名且公开的：任何畸形输入都会有人送进来，一次未捕获异常
        // 就是一条 500 加一行噪音日志。
        CreateService().ResolveUnsubscribeToken(token).ShouldBeNull();
    }

    [Fact]
    public void Issuing_a_token_without_a_configured_secret_throws_rather_than_using_a_default()
    {
        // ★ 刻意抛而不是回退到内置默认密钥：默认密钥人人都知道，等于签名不存在，
        //   而这种失效毫无症状。宁可在部署时炸，也不要发出一批可伪造的链接。
        var service = CreateService(secret: null);

        var ex = Should.Throw<InvalidOperationException>(
            () => service.CreateUnsubscribeToken("alice@example.com", NotificationType.Email));
        ex.Message.ShouldContain("TokenSecret");
    }
}
