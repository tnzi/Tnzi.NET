namespace Tnzi.Payment.Promotions.Tests.Integration;

/// <summary>
/// 持券消费的抢占，以及「抢不到就必须失败」。
/// </summary>
/// <remarks>
/// 本组覆盖的是**顺序执行**下能观察到的那部分：券被抢占、被链接到核销记录、
/// 多张券逐张消耗、公开促销没有券也照常放行。
/// 并发那一半（校验之后、写入之前券被别人抢走）顺序执行时走不到 ——
/// 事务外那次校验总是先拒绝 —— 由 <see cref="CouponRaceIntegrationTests"/> 用一个
/// 确定性的交错构造覆盖。两组缺一不可：只有这一组时，把事务内那两道守卫整个删掉，
/// 本文件全绿。
/// </remarks>
public class CouponClaimIntegrationTests : PromotionsIntegrationTestBase
{
    private async Task<Promotion> SeedPromotionAsync(bool isPublic, int? perUserLimit = null)
    {
        var promotion = new Promotion
        {
            PromotionCode = "HELD10",
            Name = "Held 10%",
            IsActive = true,
            IsPublic = isPublic,
            StartTime = DateTime.UtcNow.AddDays(-1),
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            Currency = "USD",
            Stackable = true,
            ProductType = ProductType.All,
            ApplyScope = ApplyScope.Global,
            PerUserUsageLimit = perUserLimit,
            UsedCount = 0
        };
        await SeedAsync(promotion);
        return promotion;
    }

    private async Task<UserCoupon> SeedUserCouponAsync(Guid userId, Guid promotionId)
    {
        var coupon = new UserCoupon
        {
            UserId = userId,
            PromotionId = promotionId,
            Status = UserCouponStatus.Available,
            AcquiredTime = DateTime.UtcNow
        };
        await SeedAsync(coupon);
        return coupon;
    }

    private Task<Result<CouponUsageDto>> ApplyAsync(Guid userId, string orderNo) =>
        InScopeAsync<ICouponService, Result<CouponUsageDto>>(svc => svc.ApplyCouponAsync(new CouponApplyContext
        {
            CouponCode = "HELD10",
            UserId = userId,
            BusinessOrderNo = orderNo,
            OrderAmount = 100m,
            Currency = "USD",
            ProductType = ProductType.All
        }));

    [Fact]
    public async Task AHeldCoupon_IsConsumedAndLinkedToTheUsage()
    {
        var user = Guid.NewGuid();
        var promotion = await SeedPromotionAsync(isPublic: false);
        var coupon = await SeedUserCouponAsync(user, promotion.Id);

        var applied = await ApplyAsync(user, "ORDER-A");

        applied.Succeeded.ShouldBeTrue();
        (await ReloadAsync<CouponUsage>(applied.Data!.Id))!.UserCouponId.ShouldBe(coupon.Id);

        var reloaded = await ReloadAsync<UserCoupon>(coupon.Id);
        reloaded!.Status.ShouldBe(UserCouponStatus.Used);
        reloaded.UsedTime.ShouldNotBeNull();
        reloaded.CouponUsageId.ShouldBe(applied.Data.Id);
    }

    /// <summary>
    /// 公开促销不要求持券：没有可抢的券时照常放行，核销记录上的券 Id 为空 ——
    /// 这是事实，不是失败。收得太紧会把「输码即用」的促销整条堵死。
    /// </summary>
    [Fact]
    public async Task APublicPromotionWithoutAHeldCoupon_StillApplies()
    {
        var user = Guid.NewGuid();
        await SeedPromotionAsync(isPublic: true);

        var applied = await ApplyAsync(user, "ORDER-B");

        applied.Succeeded.ShouldBeTrue();
        (await ReloadAsync<CouponUsage>(applied.Data!.Id))!.UserCouponId.ShouldBeNull();
    }

    /// <summary>
    /// 持券的用户在同一张券上只能拿到一次折扣。
    /// </summary>
    /// <remarks>
    /// 顺序执行时拒绝发生在事务外那次校验上（非公开促销要求持有可用券），
    /// 不是事务内那道守卫 —— 这条钉的是「结果对不对」，不是「哪一层拦住的」。
    /// </remarks>
    [Fact]
    public async Task AUserHoldingOneCoupon_GetsTheDiscountOnlyOnce()
    {
        var user = Guid.NewGuid();
        var promotion = await SeedPromotionAsync(isPublic: false);
        await SeedUserCouponAsync(user, promotion.Id);

        (await ApplyAsync(user, "ORDER-C1")).Succeeded.ShouldBeTrue();

        var second = await ApplyAsync(user, "ORDER-C2");
        second.Succeeded.ShouldBeFalse();

        // 被拒的那一次不能烧掉总用量名额
        (await ReloadAsync<Promotion>(promotion.Id))!.UsedCount.ShouldBe(1);
    }

    /// <summary>
    /// 持两张券就能用两次 —— 抢占是按 Id 逐张抢的，不能一次把该用户所有可用券全标成已用。
    /// </summary>
    [Fact]
    public async Task AUserHoldingTwoCoupons_ConsumesThemOneAtATime()
    {
        var user = Guid.NewGuid();
        var promotion = await SeedPromotionAsync(isPublic: false);
        var first = await SeedUserCouponAsync(user, promotion.Id);
        var second = await SeedUserCouponAsync(user, promotion.Id);

        var a = await ApplyAsync(user, "ORDER-D1");
        var b = await ApplyAsync(user, "ORDER-D2");

        a.Succeeded.ShouldBeTrue();
        b.Succeeded.ShouldBeTrue();

        var claimed = new[]
        {
            (await ReloadAsync<CouponUsage>(a.Data!.Id))!.UserCouponId,
            (await ReloadAsync<CouponUsage>(b.Data!.Id))!.UserCouponId
        };
        claimed.ShouldBe([first.Id, second.Id], ignoreOrder: true);
    }

    /// <summary>
    /// 超过每用户次数的那一笔不能烧掉总用量名额。
    /// </summary>
    /// <remarks>
    /// 顺序执行时这一笔被事务外的校验挡下，根本没递增过；
    /// 事务内那条路径（递增之后才发现要拒绝，因此要补偿）由
    /// <see cref="CouponRaceIntegrationTests"/> 覆盖。
    /// </remarks>
    [Fact]
    public async Task ARefusalOnTheUserLimit_DoesNotBurnATotalUsageSlot()
    {
        var user = Guid.NewGuid();
        var promotion = await SeedPromotionAsync(isPublic: true, perUserLimit: 1);

        (await ApplyAsync(user, "ORDER-E1")).Succeeded.ShouldBeTrue();
        (await ReloadAsync<Promotion>(promotion.Id))!.UsedCount.ShouldBe(1);

        var second = await ApplyAsync(user, "ORDER-E2");
        second.Succeeded.ShouldBeFalse();
        second.Message.ShouldBe(ErrorCodes.CouponUsageLimitReached);

        (await ReloadAsync<Promotion>(promotion.Id))!.UsedCount.ShouldBe(1);
    }
}
