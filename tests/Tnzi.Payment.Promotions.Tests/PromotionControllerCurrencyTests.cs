using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tnzi.Payment.Options;
using Tnzi.Payment.Promotions.Controllers;
using Tnzi.Security.Claims;

namespace Tnzi.Payment.Promotions.Tests;

/// <summary>
/// 付款人侧的试算端点不带币种时，按本部署配置的默认币种校验，而不是写死的 USD。
/// </summary>
/// <remarks>
/// 建单时不带币种的订单落在 <c>Payment:DefaultCurrency</c> 上，不带币种建的促销也落在那里；
/// 试算若仍按常量 USD 校验，配了 CAD 的商户里同一张券在试算时被判「币种不符」而在结账时通过：
/// 结账页会把一张能用的券提示成不能用。
/// </remarks>
public class PromotionControllerCurrencyTests
{
    private static (DefaultPromotionController Controller, Func<CouponApplyContext?> Seen) Build(string defaultCurrency)
    {
        CouponApplyContext? seen = null;
        var promotions = new Mock<IPromotionService>();
        promotions
            .Setup(s => s.ValidateCouponAsync(It.IsAny<CouponApplyContext>(), It.IsAny<CancellationToken>()))
            .Callback<CouponApplyContext, CancellationToken>((c, _) => seen = c)
            .ReturnsAsync(Result.Success(new CouponValidationResultDto { IsValid = false }));
        promotions
            .Setup(s => s.CalculateDiscountAsync(It.IsAny<CouponApplyContext>(), It.IsAny<CancellationToken>()))
            .Callback<CouponApplyContext, CancellationToken>((c, _) => seen = c)
            .ReturnsAsync(Result.Success(new DiscountCalculationResultDto()));

        var paymentOptions = new Mock<IOptionsMonitor<PaymentOptions>>();
        paymentOptions.Setup(m => m.CurrentValue).Returns(new PaymentOptions { DefaultCurrency = defaultCurrency });

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns(Guid.NewGuid());
        var services = new ServiceCollection().AddSingleton(currentUser.Object).BuildServiceProvider();

        var controller = new DefaultPromotionController(promotions.Object, new Mock<ICouponWalletService>().Object, paymentOptions.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } }
        };
        return (controller, () => seen);
    }

    [Fact]
    public async Task ValidateCoupon_WithoutCurrency_UsesTheDeploymentDefault()
    {
        var (controller, seen) = Build("CAD");

        await controller.ValidateCoupon(new ValidateCouponDto { CouponCode = "SAVE5", OrderAmount = 50m });

        seen()!.Currency.ShouldBe("CAD");
    }

    [Fact]
    public async Task CalculateDiscount_WithoutCurrency_UsesTheDeploymentDefault()
    {
        var (controller, seen) = Build("CAD");

        await controller.CalculateDiscount(new CalculateDiscountDto { CouponCode = "SAVE5", OrderAmount = 50m });

        seen()!.Currency.ShouldBe("CAD");
    }

    [Fact]
    public async Task ValidateCoupon_WithCurrency_KeepsTheCallersCurrency()
    {
        var (controller, seen) = Build("CAD");

        await controller.ValidateCoupon(new ValidateCouponDto { CouponCode = "SAVE5", OrderAmount = 50m, Currency = "EUR" });

        seen()!.Currency.ShouldBe("EUR");
    }
}
