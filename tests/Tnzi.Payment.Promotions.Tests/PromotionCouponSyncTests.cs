using System.Linq.Expressions;
using Microsoft.Extensions.Logging;

namespace Tnzi.Payment.Promotions.Tests;

/// <summary>
/// 促销同步到渠道的三条分支：没有实现 / 实现是别的渠道 / 实现就位。
/// </summary>
/// <remarks>
/// Stripe 的实现住在可选子模块 <c>Tnzi.Payment.Stripe</c>，本模块只有契约。
/// 这里锁的是**缺席时的行为**：必须失败并指名要加载什么，绝不能报成功 ——
/// 报成功等于告诉运营「渠道侧已经有这张券了」，而真相要等到某个用户
/// 在渠道结账页输码被拒时才暴露。
/// </remarks>
public class PromotionCouponSyncTests
{
    private readonly Mock<IRepository<Promotion, Guid>> _promotionRepositoryMock = new();
    private readonly Mock<IOptionsMonitor<PromotionOptions>> _promotionOptionsMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();

    public PromotionCouponSyncTests()
    {
        _promotionOptionsMock.Setup(x => x.CurrentValue)
            .Returns(new PromotionOptions { EnableStripeCouponSync = true });

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);
    }

    private PromotionService CreateService(IPaymentChannelCouponSync? couponSync)
        => new(
            _promotionRepositoryMock.Object,
            new Mock<IRepository<CouponUsage, Guid>>().Object,
            new Mock<IRepository<UserCoupon, Guid>>().Object,
            _promotionOptionsMock.Object,
            _serviceProviderMock.Object,
            couponSync);

    private Promotion ArrangePromotion()
    {
        var promotion = new Promotion
        {
            Id = Guid.NewGuid(),
            PromotionCode = "SAVE10",
            Name = "Save 10",
            DiscountType = DiscountType.Fixed,
            DiscountValue = 10m,
            Currency = "EUR"
        };

        _promotionRepositoryMock
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<Promotion, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(promotion);

        return promotion;
    }

    [Fact]
    public async Task SyncToStripeAsync_WithoutImplementation_FailsAndNamesTheModule()
    {
        // Arrange
        var promotion = ArrangePromotion();
        var service = CreateService(couponSync: null);

        // Act
        var result = await service.SyncToStripeAsync(promotion.Id);

        // Assert - 少能力，不是错行为：当场失败，并说清楚少了哪个包
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
        result.Message.ShouldNotBeNull();
        result.Message!.ShouldContain("Tnzi.Payment.Stripe");

        // 没有实现时绝不能写 StripeCouponId：写了就等于给一张不存在的渠道券开凭证
        promotion.StripeCouponId.ShouldBeNull();
        _promotionRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<Promotion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncToStripeAsync_WithImplementationForAnotherChannel_Fails()
    {
        // Arrange - 某个应用注册了别的渠道的实现
        var promotion = ArrangePromotion();
        var foreignSync = new Mock<IPaymentChannelCouponSync>();
        foreignSync.SetupGet(x => x.ChannelCode).Returns("PayPal");

        var service = CreateService(foreignSync.Object);

        // Act
        var result = await service.SyncToStripeAsync(promotion.Id);

        // Assert - 打到别家去而本地照写 StripeCouponId，是最难查的那种「成功」
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
        foreignSync.Verify(
            x => x.SyncCouponAsync(It.IsAny<PaymentChannelCouponDto>(), It.IsAny<CancellationToken>()), Times.Never);
        promotion.StripeCouponId.ShouldBeNull();
    }

    [Fact]
    public async Task SyncToStripeAsync_WithStripeImplementation_StoresReturnedCouponId()
    {
        // Arrange
        var promotion = ArrangePromotion();
        PaymentChannelCouponDto? captured = null;

        var stripeSync = new Mock<IPaymentChannelCouponSync>();
        stripeSync.SetupGet(x => x.ChannelCode).Returns(PaymentConstants.StripeChannelCode);
        stripeSync
            .Setup(x => x.SyncCouponAsync(It.IsAny<PaymentChannelCouponDto>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentChannelCouponDto, CancellationToken>((dto, _) => captured = dto)
            .ReturnsAsync(Result<string>.Success("coupon_SAVE10"));

        var service = CreateService(stripeSync.Object);

        // Act
        var result = await service.SyncToStripeAsync(promotion.Id);

        // Assert
        result.Succeeded.ShouldBeTrue();
        promotion.StripeCouponId.ShouldBe("coupon_SAVE10");

        // 币种取促销自身的币种：固定金额折扣与币种强相关，传错渠道侧会按另一种货币建券
        captured.ShouldNotBeNull();
        captured!.Currency.ShouldBe("EUR");
        captured.PromotionCode.ShouldBe("SAVE10");
    }

    [Fact]
    public async Task SyncToStripeAsync_WhenDisabledByConfiguration_DoesNotReachTheChannel()
    {
        // Arrange
        _promotionOptionsMock.Setup(x => x.CurrentValue)
            .Returns(new PromotionOptions { EnableStripeCouponSync = false });

        var stripeSync = new Mock<IPaymentChannelCouponSync>();
        stripeSync.SetupGet(x => x.ChannelCode).Returns(PaymentConstants.StripeChannelCode);

        var service = CreateService(stripeSync.Object);

        // Act
        var result = await service.SyncToStripeAsync(Guid.NewGuid());

        // Assert - 配置关掉是「不想同步」，与「没装包」是两回事，仍走既有的 400
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        stripeSync.Verify(
            x => x.SyncCouponAsync(It.IsAny<PaymentChannelCouponDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
