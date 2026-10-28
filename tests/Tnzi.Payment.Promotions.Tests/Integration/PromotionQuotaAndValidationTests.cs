namespace Tnzi.Payment.Promotions.Tests.Integration;

/// <summary>
/// 券包与核销守卫用同一个「每用户上限」；百分比折扣不超过 100；兑换码数量的错误码说的是数量。
/// </summary>
public class PromotionQuotaAndValidationTests : PromotionsIntegrationTestBase
{
    /// <summary><c>PromotionOptions.MaxCouponUsagePerUser</c> 的出厂值。</summary>
    private const int DefaultPerUserLimit = 5;

    private async Task<Promotion> SeedPublicPromotionAsync(int? perUserLimit)
    {
        var promotion = new Promotion
        {
            PromotionCode = $"P{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
            Name = "Spring",
            IsActive = true,
            IsPublic = true,
            StartTime = DateTime.UtcNow.AddDays(-1),
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            Currency = "USD",
            Stackable = true,
            ProductType = ProductType.All,
            ApplyScope = ApplyScope.Global,
            PerUserUsageLimit = perUserLimit
        };
        await SeedAsync(promotion);
        return promotion;
    }

    private async Task SeedUsagesAsync(Promotion promotion, Guid userId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await SeedAsync(new CouponUsage
            {
                CouponId = promotion.Id,
                UserId = userId,
                DiscountAmount = 1m,
                BusinessOrderNo = $"ORD-{Guid.NewGuid():N}",
                OrderSlot = 0
            });
        }
    }

    private Task<Result<List<UserCouponDto>>> WalletAsync(Guid userId)
        => InScopeAsync<ICouponWalletService, Result<List<UserCouponDto>>>(s => s.GetUserAvailableCouponsAsync(userId));

    /// <summary>★ 没单独设上限的促销按配置的默认值收口：用满之后不再出现在券包里（结账会被拒）。</summary>
    [Fact]
    public async Task Wallet_HidesAPromotion_OnceTheDefaultPerUserLimitIsUsedUp()
    {
        var promotion = await SeedPublicPromotionAsync(perUserLimit: null);
        var user = Guid.NewGuid();
        await SeedUsagesAsync(promotion, user, DefaultPerUserLimit);

        var wallet = await WalletAsync(user);

        wallet.Data!.ShouldNotContain(c => c.Id == promotion.Id, "守卫按默认上限拒绝的券，券包不能还显示可用");
    }

    /// <summary>★ 剩余次数按默认上限算，而不是 -1（不限）。</summary>
    [Fact]
    public async Task Wallet_CountsRemainingUses_AgainstTheDefaultLimit()
    {
        var promotion = await SeedPublicPromotionAsync(perUserLimit: null);
        var user = Guid.NewGuid();
        await SeedUsagesAsync(promotion, user, 2);

        var wallet = await WalletAsync(user);

        wallet.Data!.Single(c => c.Id == promotion.Id).RemainingUsageCount.ShouldBe(DefaultPerUserLimit - 2);
    }

    /// <summary>★ 上限设为 0 与守卫一样视为不限：券包照常列出，剩余次数为 -1，守卫也放行。</summary>
    [Fact]
    public async Task Wallet_TreatsAZeroLimitAsUnlimited_LikeTheGuard()
    {
        var promotion = await SeedPublicPromotionAsync(perUserLimit: 0);
        var user = Guid.NewGuid();
        await SeedUsagesAsync(promotion, user, 1);

        var wallet = await WalletAsync(user);
        var validation = await InScopeAsync<IPromotionService, Result<CouponValidationResultDto>>(s => s.ValidateCouponAsync(new CouponApplyContext
        {
            CouponCode = promotion.PromotionCode,
            UserId = user,
            BusinessOrderNo = "ORD-NEXT",
            OrderAmount = 100m,
            Currency = "USD",
            ProductType = ProductType.All
        }));

        wallet.Data!.Single(c => c.Id == promotion.Id).RemainingUsageCount.ShouldBe(-1);
        validation.Data!.IsValid.ShouldBeTrue(validation.Data.ErrorMessage);
    }

    /// <summary>★ 百分比折扣不能超过 100（建与改都拦）；固定金额折扣不受这条约束。</summary>
    [Fact]
    public async Task Promotions_RefuseAPercentageAbove100()
    {
        var create = await InScopeAsync<IPromotionService, Result<PromotionDto>>(s => s.CreateAsync(new CreatePromotionDto
        {
            PromotionCode = "OVER100",
            Name = "Too generous",
            Type = PromotionType.PercentageDiscount,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 150m
        }));
        var fixedAmount = await InScopeAsync<IPromotionService, Result<PromotionDto>>(s => s.CreateAsync(new CreatePromotionDto
        {
            PromotionCode = "FIXED150",
            Name = "Fixed",
            Type = PromotionType.FixedAmountDiscount,
            DiscountType = DiscountType.Fixed,
            DiscountValue = 150m
        }));
        var percentage = await SeedPublicPromotionAsync(perUserLimit: null);
        var update = await InScopeAsync<IPromotionService, Result>(s => s.UpdateAsync(percentage.Id, new UpdatePromotionDto { DiscountValue = 101m }));

        create.Succeeded.ShouldBeFalse();
        create.Code.ShouldBe(400);
        fixedAmount.Succeeded.ShouldBeTrue(fixedAmount.Message);
        update.Succeeded.ShouldBeFalse();
        update.Code.ShouldBe(400);
        (await ReloadAsync<Promotion>(percentage.Id))!.DiscountValue.ShouldBe(10m);
    }

    /// <summary>数量不大于 0 答数量错误，不是「兑换码已达使用上限」。</summary>
    [Fact]
    public async Task CreateRedemptionCode_WithNoQuantity_AnswersTheQuantityError()
    {
        var promotion = await SeedPublicPromotionAsync(perUserLimit: null);

        var result = await InScopeAsync<ICouponIssuanceService, Result<string>>(s => s.CreateRedemptionCodeAsync(promotion.Id, 0));

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.RedemptionCodeInvalidQuantity);
    }
}
