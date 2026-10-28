using Microsoft.Extensions.Options;

namespace Tnzi.Payment.PayPal.Tests;

/// <summary>
/// 启用 PayPal 不再要求一个没有任何代码读取的 <c>Currency</c>：建单币种走
/// <c>Payment:Channels:PayPal:Currency</c> 与 <c>Payment:DefaultCurrency</c>。
/// </summary>
public class PayPalOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(PayPalOptions options) => new PayPalOptionsValidator().Validate(null, options);

    [Fact]
    public void Enabled_WithCredentialsAndWebhookId_IsValid()
    {
        Validate(new PayPalOptions { Enabled = true, ClientId = "client", ClientSecret = "secret", WebhookId = "webhook", Mode = "sandbox" })
            .Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Enabled_WithoutTheWebhookId_IsRefused()
    {
        var result = Validate(new PayPalOptions { Enabled = true, ClientId = "client", ClientSecret = "secret", Mode = "sandbox" });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("WebhookId");
    }
}
