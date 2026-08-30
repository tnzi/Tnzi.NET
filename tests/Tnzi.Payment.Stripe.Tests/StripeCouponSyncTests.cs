namespace Tnzi.Payment.Stripe.Tests;

/// <summary>
/// StripeCouponSync 单元测试。
/// </summary>
/// <remarks>
/// 真正的同步要打 Stripe，这里只锁一件不触网但承重的事：<see cref="IPaymentChannelCouponSync.ChannelCode"/>。
/// 父模块的 <c>PromotionService.SyncToStripeAsync</c> 拿它比对，不等就拒绝同步 ——
/// 这个属性写错，表现是「本地记了 StripeCouponId，券却建在别家」。
/// </remarks>
public class StripeCouponSyncTests
{
    private static StripeCouponSync CreateSync()
    {
        var options = new OptionsWrapper<StripeOptions>(new StripeOptions
        {
            Enabled = true,
            SecretKey = "sk_test_key",
            PublishableKey = "pk_test_key",
            WebhookSecret = "whsec_test",
            Currency = "usd"
        });

        return new StripeCouponSync(options, new Mock<ILogger<StripeCouponSync>>().Object);
    }

    [Fact]
    public void ChannelCode_MatchesTheCodeThePromotionServiceChecks()
    {
        CreateSync().ChannelCode.ShouldBe(PaymentConstants.StripeChannelCode);
    }

    [Fact]
    public void ChannelCode_MatchesTheProviderInTheSamePackage()
    {
        // 同一个包里的两个实现必须自称同一个渠道，否则「同步用的是 Stripe、收款用的是别家」
        var provider = new StripeProvider(
            new OptionsWrapper<StripeOptions>(new StripeOptions { SecretKey = "sk_test_key" }),
            new Mock<ILogger<StripeProvider>>().Object);

        CreateSync().ChannelCode.ShouldBe(provider.ChannelCode);
    }
}
