namespace Tnzi.Payment.Stripe.Tests;

/// <summary>
/// 启用 Stripe 时只要求框架真的会用到的两项：<c>SecretKey</c> 与 <c>WebhookSecret</c>。
/// </summary>
/// <remarks>
/// 此前还要求 <c>Currency</c> 与 <c>PublishableKey</c>：前者没有任何代码读（建单币种走
/// <c>Payment:Channels:Stripe:Currency</c> 与 <c>Payment:DefaultCurrency</c>），后者服务端从不使用：
/// 必填一个不生效的值，只会让配成 eur 的商户以为自己在按欧元收款。
/// </remarks>
public class StripeOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(StripeOptions options) => new StripeOptionsValidator().Validate(null, options);

    [Fact]
    public void Enabled_WithTheSecretAndTheWebhookSecret_IsValid()
    {
        Validate(new StripeOptions { Enabled = true, SecretKey = "sk_test_key", WebhookSecret = "whsec_test" })
            .Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Enabled_WithoutTheWebhookSecret_IsRefused()
    {
        var result = Validate(new StripeOptions { Enabled = true, SecretKey = "sk_test_key" });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("WebhookSecret");
    }
}
