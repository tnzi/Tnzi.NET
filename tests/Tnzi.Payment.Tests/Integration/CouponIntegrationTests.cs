using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 优惠券核销资金安全集成测试（真实 SQLite，验证总量/每用户限额与订单幂等）
/// </summary>
public class CouponIntegrationTests : PaymentIntegrationTestBase
{
    private async Task<Promotion> SeedPromotionAsync(int? totalLimit, int? perUserLimit)
    {
        var promotion = new Promotion
        {
            PromotionCode = "SAVE10",
            Name = "Save 10%",
            IsActive = true,
            StartTime = DateTime.UtcNow.AddDays(-1),
            EndTime = null,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            Stackable = true,
            TotalUsageLimit = totalLimit,
            PerUserUsageLimit = perUserLimit,
            UsedCount = 0
        };
        await SeedAsync(promotion);
        return promotion;
    }

    private Task<Result<CouponUsageDto>> ApplyAsync(Guid userId, Guid orderId) =>
        InScopeAsync<ICouponService, Result<CouponUsageDto>>(
            svc => svc.ApplyCouponAsync("SAVE10", userId, orderId, 100m));

    [Fact]
    public async Task ApplyCoupon_RespectsTotalUsageLimit()
    {
        await SeedPromotionAsync(totalLimit: 1, perUserLimit: null);

        var first = await ApplyAsync(Guid.NewGuid(), Guid.NewGuid());
        var second = await ApplyAsync(Guid.NewGuid(), Guid.NewGuid());

        first.Succeeded.ShouldBeTrue();
        second.Succeeded.ShouldBeFalse();
        second.Message.ShouldBe(ErrorCodes.CouponUsageLimitReached);
    }

    [Fact]
    public async Task ApplyCoupon_RespectsPerUserLimit()
    {
        await SeedPromotionAsync(totalLimit: null, perUserLimit: 1);
        var user = Guid.NewGuid();

        var first = await ApplyAsync(user, Guid.NewGuid());
        var second = await ApplyAsync(user, Guid.NewGuid());

        first.Succeeded.ShouldBeTrue();
        second.Succeeded.ShouldBeFalse();
        second.Message.ShouldBe(ErrorCodes.CouponUsageLimitReached);
    }

    [Fact]
    public async Task ApplyCoupon_SameOrderTwice_IsIdempotent()
    {
        await SeedPromotionAsync(totalLimit: null, perUserLimit: null);
        var user = Guid.NewGuid();
        var order = Guid.NewGuid();

        var first = await ApplyAsync(user, order);
        var second = await ApplyAsync(user, order);

        first.Succeeded.ShouldBeTrue();
        second.Succeeded.ShouldBeFalse();
        second.Message.ShouldBe(ErrorCodes.CouponAlreadyUsedByUser);
    }
}
