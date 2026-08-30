namespace Tnzi.Payment.Stripe.Tests;

/// <summary>
/// StripeProvider 单元测试：只覆盖不触网的分支。
/// </summary>
/// <remarks>
/// 拆包之前 Stripe 渠道没有任何单元测试（父模块的测试里出现的 "Stripe" 全是 ChannelCode 字符串）。
/// 这里补的是几条**不需要凭据也不需要网络**就能锁死的事实：渠道代码、能力上报、
/// 以及所有在打出第一个 HTTP 请求之前就该拒绝的入参。
/// </remarks>
public class StripeProviderTests
{
    private readonly StripeProvider _provider;

    public StripeProviderTests()
    {
        var options = new OptionsWrapper<StripeOptions>(new StripeOptions
        {
            Enabled = true,
            SecretKey = "sk_test_key",
            PublishableKey = "pk_test_key",
            WebhookSecret = string.Empty,
            Currency = "usd"
        });

        _provider = new StripeProvider(options, new Mock<ILogger<StripeProvider>>().Object);
    }

    [Fact]
    public void ChannelCode_IsTheCodeTheFactoryAndConfigurationUse()
    {
        // 这个字符串同时是 PaymentProviderFactory 的字典键、Payment:Channels 的配置键，
        // 以及 Payment:DefaultChannelCode 的出厂值。改了它等于让三处同时失效。
        _provider.ChannelCode.ShouldBe(PaymentConstants.StripeChannelCode);
        _provider.ChannelCode.ShouldBe("Stripe");
    }

    [Fact]
    public void Capabilities_AreReportedAsSupported()
    {
        // 订阅自动续费按这两个标志决定走不走绑卡 + 无人值守扣款
        _provider.SupportsPaymentMethodStorage.ShouldBeTrue();
        _provider.SupportsOffSessionCharge.ShouldBeTrue();
    }

    [Theory]
    [InlineData(PaymentMethodEnum.CreditCard, true)]
    [InlineData(PaymentMethodEnum.DebitCard, true)]
    [InlineData(PaymentMethodEnum.ApplePay, true)]
    [InlineData(PaymentMethodEnum.GooglePay, true)]
    [InlineData(PaymentMethodEnum.PayPal, false)]
    [InlineData(PaymentMethodEnum.BankTransfer, false)]
    public void IsSupported_CoversCardAndWalletsOnly(PaymentMethodEnum method, bool expected)
    {
        _provider.IsSupported(method).ShouldBe(expected);
    }

    [Fact]
    public async Task VerifySignatureAsync_WithoutWebhookSecret_ReturnsFalse()
    {
        // 没配 WebhookSecret 就无法验签，必须判否 —— 判是等于任何人都能伪造回调把订单刷成已付
        var parameters = new Dictionary<string, string>
        {
            [PaymentConstants.CallbackRawBodyKey] = "{}",
            [PaymentConstants.CallbackStripeSignatureKey] = "t=1,v1=deadbeef"
        };

        (await _provider.VerifySignatureAsync(parameters)).ShouldBeFalse();
    }

    [Fact]
    public async Task VerifySignatureAsync_WithoutSignatureHeader_ReturnsFalse()
    {
        var provider = CreateProviderWithWebhookSecret();
        var parameters = new Dictionary<string, string>
        {
            [PaymentConstants.CallbackRawBodyKey] = "{}"
        };

        (await provider.VerifySignatureAsync(parameters)).ShouldBeFalse();
    }

    [Fact]
    public async Task HandleCallbackAsync_WithoutRawBody_FailsWithInvalidSignature()
    {
        var result = await _provider.HandleCallbackAsync(new Dictionary<string, string>());

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PaymentInvalidSignature);
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task ChargeOffSessionAsync_WithoutStoredToken_FailsBeforeCallingStripe()
    {
        var result = await _provider.ChargeOffSessionAsync(new PaymentProviderChargeDto
        {
            TradeNo = "T1",
            BusinessOrderNo = "O1",
            Amount = 10m,
            Currency = "USD"
        });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.SubscriptionPaymentMethodMissing);
    }

    [Fact]
    public async Task DetachPaymentMethodAsync_WithoutToken_FailsBeforeCallingStripe()
    {
        var result = await _provider.DetachPaymentMethodAsync(new PaymentProviderResolveMethodDto());

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PaymentMethodNotFound);
    }

    [Fact]
    public async Task ResolvePaymentMethodAsync_WithoutToken_FailsBeforeCallingStripe()
    {
        var result = await _provider.ResolvePaymentMethodAsync(new PaymentProviderResolveMethodDto());

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PaymentMethodNotFound);
    }

    [Fact]
    public async Task GetPaymentParamsAsync_WithInternalTradeNo_DoesNotCallStripe()
    {
        // 只有 PaymentIntent ID（pi_ 前缀）才值得回源取 ClientSecret；
        // 内部流水号直接原样返回，收银台首次建单那一刻本就还没有 intent
        var result = await _provider.GetPaymentParamsAsync("TRD20260829001");

        result.Succeeded.ShouldBeTrue();
        result.Data!.TradeNo.ShouldBe("TRD20260829001");
        result.Data.ClientSecret.ShouldBeNull();
    }

    private static StripeProvider CreateProviderWithWebhookSecret()
    {
        var options = new OptionsWrapper<StripeOptions>(new StripeOptions
        {
            Enabled = true,
            SecretKey = "sk_test_key",
            PublishableKey = "pk_test_key",
            WebhookSecret = "whsec_test",
            Currency = "usd"
        });

        return new StripeProvider(options, new Mock<ILogger<StripeProvider>>().Object);
    }
}
