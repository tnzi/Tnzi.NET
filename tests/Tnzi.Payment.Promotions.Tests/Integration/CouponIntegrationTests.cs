using Tnzi.Data;

namespace Tnzi.Payment.Promotions.Tests.Integration;

/// <summary>
/// 优惠券核销资金安全集成测试（真实 SQLite，验证总量/每用户限额、订单幂等、范围校验与释放）
/// </summary>
public class CouponIntegrationTests : PromotionsIntegrationTestBase
{
    private async Task<Promotion> SeedPromotionAsync(
        int? totalLimit,
        int? perUserLimit,
        bool isPublic = true,
        ApplyScope applyScope = ApplyScope.Global,
        List<Guid>? scopeIds = null,
        ProductType productType = ProductType.All,
        bool firstSubscriptionOnly = false)
    {
        var promotion = new Promotion
        {
            PromotionCode = "SAVE10",
            Name = "Save 10%",
            IsActive = true,
            IsPublic = isPublic,
            StartTime = DateTime.UtcNow.AddDays(-1),
            EndTime = null,
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            Currency = "USD",
            Stackable = true,
            ProductType = productType,
            ApplyScope = applyScope,
            ScopeIdsJson = scopeIds != null ? System.Text.Json.JsonSerializer.Serialize(scopeIds) : null,
            FirstSubscriptionOnly = firstSubscriptionOnly,
            TotalUsageLimit = totalLimit,
            PerUserUsageLimit = perUserLimit,
            UsedCount = 0
        };
        await SeedAsync(promotion);
        return promotion;
    }

    private static CouponApplyContext Context(Guid userId, string orderNo, Guid? scopeId = null, ProductType productType = ProductType.All) => new()
    {
        CouponCode = "SAVE10",
        UserId = userId,
        BusinessOrderNo = orderNo,
        OrderAmount = 100m,
        Currency = "USD",
        ProductType = productType,
        ScopeId = scopeId
    };

    private Task<Result<CouponUsageDto>> ApplyAsync(Guid userId, string orderNo, Guid? scopeId = null, ProductType productType = ProductType.All) =>
        InScopeAsync<ICouponService, Result<CouponUsageDto>>(
            svc => svc.ApplyCouponAsync(Context(userId, orderNo, scopeId, productType)));

    [Fact]
    public async Task ApplyCoupon_InsideCallerTransaction_RollbackRestoresTheUsageSlot()
    {
        // ★ 守的是 ApplyCouponAsync 里 CAS 递增前的 EnsureTransactionStartedAsync：
        //   物理事务延迟到首次 flush 才 BEGIN，而 CAS 递增（裸 SQL）是这条路径的首个写操作。
        //   没有前置时它逃逸出事务——使用记录随回滚消失，UsedCount 却永久 +1，
        //   一张谁也没用过的券就此烧掉，且没有任何报错。
        await SeedPromotionAsync(totalLimit: 1, perUserLimit: null);

        using (var scope = ServiceProvider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var svc = scope.ServiceProvider.GetRequiredService<ICouponService>();

            manager.EnableTransaction();
            var applied = await svc.ApplyCouponAsync(Context(Guid.NewGuid(), "ORDER-RB"));
            applied.Succeeded.ShouldBeTrue(applied.Message);
            await manager.RollbackTransactionAsync();
        }

        // 回滚后名额必须还回来：总量上限 1，第二次核销仍然要成功
        var again = await ApplyAsync(Guid.NewGuid(), "ORDER-RB-2");
        again.Succeeded.ShouldBeTrue(again.Message);
    }

    [Fact]
    public async Task ReleaseCoupon_InsideCallerTransaction_RollbackKeepsTheSlotTaken()
    {
        // ★ 守的是 ReleaseCouponAsync 里递减 UsedCount（裸 SQL）前的 EnsureTransactionStartedAsync：
        //   没有前置时递减逃逸出事务——使用记录随回滚复活，名额却已永久放出，同一张券被用两次。
        await SeedPromotionAsync(totalLimit: 1, perUserLimit: null);
        var applied = await ApplyAsync(Guid.NewGuid(), "ORDER-REL-1");
        applied.Succeeded.ShouldBeTrue(applied.Message);

        using (var scope = ServiceProvider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var svc = scope.ServiceProvider.GetRequiredService<ICouponService>();

            manager.EnableTransaction();
            (await svc.ReleaseCouponAsync(applied.Data!.Id)).Succeeded.ShouldBeTrue();
            await manager.RollbackTransactionAsync();
        }

        // 回滚后名额必须仍被占着：总量上限 1，第二个用户核销必须失败
        var second = await ApplyAsync(Guid.NewGuid(), "ORDER-REL-2");
        second.Succeeded.ShouldBeFalse();
        second.Message.ShouldBe(ErrorCodes.CouponUsageLimitReached);
    }

    [Fact]
    public async Task Redeem_InsideCallerTransaction_RollbackReturnsTheSlot()
    {
        // ★ 守的是 RedeemAsync 里兑换数量 CAS（裸 SQL）前的 EnsureTransactionStartedAsync：
        //   没有前置时递增（以及"兑完置过期"）逃逸出事务——持券随回滚消失，
        //   兑换名额却被永久烧掉，下一个人对着一个没人持有的码吃闭门羹。
        var promotion = await SeedPromotionAsync(totalLimit: null, perUserLimit: null, isPublic: false);
        var codeResult = await InScopeAsync<ICouponIssuanceService, Result<string>>(
            s => s.CreateRedemptionCodeAsync(promotion.Id, 1));
        codeResult.Succeeded.ShouldBeTrue(codeResult.Message);
        var code = codeResult.Data!;

        using (var scope = ServiceProvider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var svc = scope.ServiceProvider.GetRequiredService<ICouponWalletService>();

            manager.EnableTransaction();
            (await svc.RedeemAsync(code, Guid.NewGuid())).Succeeded.ShouldBeTrue();
            await manager.RollbackTransactionAsync();
        }

        // 回滚后名额必须还回来：总量 1 的码，第二个用户仍然要能兑到
        var second = await InScopeAsync<ICouponWalletService, Result<UserCouponDto>>(
            s => s.RedeemAsync(code, Guid.NewGuid()));
        second.Succeeded.ShouldBeTrue(second.Message);
    }

    [Fact]
    public async Task ApplyCoupon_RespectsTotalUsageLimit()
    {
        await SeedPromotionAsync(totalLimit: 1, perUserLimit: null);

        var first = await ApplyAsync(Guid.NewGuid(), "ORDER-1");
        var second = await ApplyAsync(Guid.NewGuid(), "ORDER-2");

        first.Succeeded.ShouldBeTrue();
        second.Succeeded.ShouldBeFalse();
        second.Message.ShouldBe(ErrorCodes.CouponUsageLimitReached);
    }

    [Fact]
    public async Task ApplyCoupon_RespectsPerUserLimit()
    {
        await SeedPromotionAsync(totalLimit: null, perUserLimit: 1);
        var user = Guid.NewGuid();

        var first = await ApplyAsync(user, "ORDER-1");
        var second = await ApplyAsync(user, "ORDER-2");

        first.Succeeded.ShouldBeTrue();
        second.Succeeded.ShouldBeFalse();
        second.Message.ShouldBe(ErrorCodes.CouponUsageLimitReached);
    }

    /// <summary>
    /// 同一业务单号重复核销必须幂等返回既有记录：
    /// 支付创建链路会重试，报错会让调用方误判"券不可用"。
    /// </summary>
    [Fact]
    public async Task ApplyCoupon_SameOrderTwice_ReturnsSameUsage()
    {
        await SeedPromotionAsync(totalLimit: null, perUserLimit: null);
        var user = Guid.NewGuid();

        var first = await ApplyAsync(user, "ORDER-1");
        var second = await ApplyAsync(user, "ORDER-1");

        first.Succeeded.ShouldBeTrue();
        second.Succeeded.ShouldBeTrue();
        second.Data!.Id.ShouldBe(first.Data!.Id);

        var promotion = await ReloadPromotionAsync();
        promotion.UsedCount.ShouldBe(1);
    }

    /// <summary>
    /// 限定计划范围的促销不能用在别的计划上（此前 ApplyScope/ScopeIds 存了但从不参与判定）
    /// </summary>
    [Fact]
    public async Task ApplyCoupon_OutOfScope_IsRejected()
    {
        var allowedPlanId = Guid.NewGuid();
        await SeedPromotionAsync(totalLimit: null, perUserLimit: null,
            applyScope: ApplyScope.Plan, scopeIds: [allowedPlanId]);

        var matched = await ApplyAsync(Guid.NewGuid(), "ORDER-1", scopeId: allowedPlanId);
        var mismatched = await ApplyAsync(Guid.NewGuid(), "ORDER-2", scopeId: Guid.NewGuid());

        matched.Succeeded.ShouldBeTrue();
        mismatched.Succeeded.ShouldBeFalse();
        mismatched.Message.ShouldBe(ErrorCodes.CouponScopeMismatch);
    }

    /// <summary>
    /// 产品类型不匹配的促销不适用（订阅券不能用在充值单上）
    /// </summary>
    [Fact]
    public async Task ApplyCoupon_ProductTypeMismatch_IsRejected()
    {
        await SeedPromotionAsync(totalLimit: null, perUserLimit: null, productType: ProductType.Subscription);

        var result = await ApplyAsync(Guid.NewGuid(), "ORDER-1", productType: ProductType.Recharge);

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.CouponScopeMismatch);
    }

    /// <summary>
    /// 非公开促销必须先领取才能用，否则任何人猜到促销码就能用
    /// </summary>
    [Fact]
    public async Task ApplyCoupon_PrivatePromotionWithoutHolding_IsRejected()
    {
        await SeedPromotionAsync(totalLimit: null, perUserLimit: null, isPublic: false);

        var result = await ApplyAsync(Guid.NewGuid(), "ORDER-1");

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.CouponNotHeld);
    }

    /// <summary>
    /// 释放后总用量回退且持券恢复可用：渠道下单失败不该让用户白丢一张券
    /// </summary>
    [Fact]
    public async Task ReleaseCoupon_RestoresQuotaAndHeldCoupon()
    {
        var promotion = await SeedPromotionAsync(totalLimit: 5, perUserLimit: null, isPublic: false);
        var user = Guid.NewGuid();

        var userCoupon = new UserCoupon
        {
            UserId = user,
            PromotionId = promotion.Id,
            Status = UserCouponStatus.Available,
            AcquiredTime = DateTime.UtcNow
        };
        await SeedAsync(userCoupon);

        var applied = await ApplyAsync(user, "ORDER-1");
        applied.Succeeded.ShouldBeTrue();

        (await ReloadPromotionAsync()).UsedCount.ShouldBe(1);
        (await ReloadAsync<UserCoupon>(userCoupon.Id))!.Status.ShouldBe(UserCouponStatus.Used);

        var released = await InScopeAsync<ICouponService, Result>(
            svc => svc.ReleaseCouponAsync(applied.Data!.Id));
        released.Succeeded.ShouldBeTrue();

        (await ReloadPromotionAsync()).UsedCount.ShouldBe(0);
        (await ReloadAsync<UserCoupon>(userCoupon.Id))!.Status.ShouldBe(UserCouponStatus.Available);
    }

    /// <summary>
    /// 兑换码兑换必须真的产出一张用户持券，而不只是给计数器加一
    /// </summary>
    [Fact]
    public async Task Redeem_GrantsUserCoupon()
    {
        var promotion = await SeedPromotionAsync(totalLimit: null, perUserLimit: null, isPublic: false);
        var code = new RedemptionCode
        {
            Code = "WELCOME2026",
            PromotionId = promotion.Id,
            Type = RedemptionCodeType.General,
            Status = RedemptionCodeStatus.Active,
            TotalQuantity = 10,
            RedeemedQuantity = 0,
            ValidFrom = DateTime.UtcNow.AddDays(-1)
        };
        await SeedAsync(code);

        var user = Guid.NewGuid();
        var redeemed = await InScopeAsync<ICouponWalletService, Result<UserCouponDto>>(
            svc => svc.RedeemAsync("WELCOME2026", user));

        redeemed.Succeeded.ShouldBeTrue();
        redeemed.Data!.IsHeld.ShouldBeTrue();
        redeemed.Data.UserCouponId.ShouldNotBeNull();

        // 领取后即可核销
        var applied = await ApplyAsync(user, "ORDER-1");
        applied.Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// 兑换码的每用户领取上限按持券计数生效（此前按核销记录计数，永远不会触发）
    /// </summary>
    [Fact]
    public async Task Redeem_RespectsPerUserLimit()
    {
        var promotion = await SeedPromotionAsync(totalLimit: null, perUserLimit: null, isPublic: false);
        var code = new RedemptionCode
        {
            Code = "ONEPERUSER",
            PromotionId = promotion.Id,
            Type = RedemptionCodeType.General,
            Status = RedemptionCodeStatus.Active,
            TotalQuantity = 10,
            RedeemedQuantity = 0,
            PerUserLimit = 1,
            ValidFrom = DateTime.UtcNow.AddDays(-1)
        };
        await SeedAsync(code);

        var user = Guid.NewGuid();
        var first = await InScopeAsync<ICouponWalletService, Result<UserCouponDto>>(svc => svc.RedeemAsync("ONEPERUSER", user));
        var second = await InScopeAsync<ICouponWalletService, Result<UserCouponDto>>(svc => svc.RedeemAsync("ONEPERUSER", user));

        first.Succeeded.ShouldBeTrue();
        second.Succeeded.ShouldBeFalse();
        second.Message.ShouldBe(ErrorCodes.RedemptionCodeUserLimitReached);

        // 每用户上限的判定在总量 CAS **之后**读（那一刻本事务持有兑换码行的锁，计数才是准的），
        // 因此被它拒掉时必须把刚递增的名额补回去 —— 否则每一次被拒都白烧一个名额，
        // 这个码的剩余数量会莫名其妙地少下去。
        (await ReloadAsync<RedemptionCode>(code.Id))!.RedeemedQuantity.ShouldBe(1);
    }

    /// <summary>
    /// ★★ 经服务铸出的通用码默认<b>每人一张</b>。此前 <c>PerUserLimit</c> 对通用码恒为 null 且没有任何设值入口，
    /// 注释援引的「促销自身的 per-user 上限」只管核销不管领取 —— 拿到一张 quantity=1000 的活动码，
    /// 一个人脚本化反复兑换就能把整批名额兑到自己名下，其余用户一律 REDEMPTION_CODE_LIMIT_REACHED，
    /// 账面「已兑 1000/1000」看不出异常。上面那条 <c>Redeem_RespectsPerUserLimit</c> 是直接 Seed 出
    /// PerUserLimit=1 的实体，服务层此前造不出那个状态，那段守卫在生产上是死代码。
    /// </summary>
    [Fact]
    public async Task Redeem_GeneralCodeCreatedThroughTheService_DefaultsToOnePerUser()
    {
        var promotion = await SeedPromotionAsync(totalLimit: null, perUserLimit: null, isPublic: false);
        var minted = await InScopeAsync<ICouponIssuanceService, Result<string>>(
            svc => svc.CreateRedemptionCodeAsync(promotion.Id, 10));
        minted.Succeeded.ShouldBeTrue(minted.Message);

        var user = Guid.NewGuid();
        var first = await InScopeAsync<ICouponWalletService, Result<UserCouponDto>>(svc => svc.RedeemAsync(minted.Data!, user));
        var second = await InScopeAsync<ICouponWalletService, Result<UserCouponDto>>(svc => svc.RedeemAsync(minted.Data!, user));

        first.Succeeded.ShouldBeTrue(first.Message);
        second.Succeeded.ShouldBeFalse("同一个人把通用码兑了第二次");
        second.Message.ShouldBe(ErrorCodes.RedemptionCodeUserLimitReached);

        var stored = await FindRedemptionCodeAsync(minted.Data!);
        stored.PerUserLimit.ShouldBe(1);
        stored.RedeemedQuantity.ShouldBe(1);

        // 别人照常领得到：上限是按人的，不是按码的
        var other = await InScopeAsync<ICouponWalletService, Result<UserCouponDto>>(svc => svc.RedeemAsync(minted.Data!, Guid.NewGuid()));
        other.Succeeded.ShouldBeTrue(other.Message);
    }

    /// <summary>显式给的每人上限原样落库；唯一码不管传什么都是 1。</summary>
    [Fact]
    public async Task CreateRedemptionCode_HonoursExplicitPerUserLimit()
    {
        var promotion = await SeedPromotionAsync(totalLimit: null, perUserLimit: null, isPublic: false);

        var general = await InScopeAsync<ICouponIssuanceService, Result<string>>(
            svc => svc.CreateRedemptionCodeAsync(promotion.Id, 10, perUserLimit: 3));
        var unique = await InScopeAsync<ICouponIssuanceService, Result<string>>(
            svc => svc.CreateRedemptionCodeAsync(promotion.Id, 1, perUserLimit: 3));

        (await FindRedemptionCodeAsync(general.Data!)).PerUserLimit.ShouldBe(3);
        (await FindRedemptionCodeAsync(unique.Data!)).PerUserLimit.ShouldBe(1);

        var user = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
            (await InScopeAsync<ICouponWalletService, Result<UserCouponDto>>(svc => svc.RedeemAsync(general.Data!, user))).Succeeded.ShouldBeTrue();
        var fourth = await InScopeAsync<ICouponWalletService, Result<UserCouponDto>>(svc => svc.RedeemAsync(general.Data!, user));
        fourth.Message.ShouldBe(ErrorCodes.RedemptionCodeUserLimitReached);
    }

    /// <summary>
    /// 「不限」必须显式要（传 0），且真的是不限 —— 那是一个写在 docs 里的、后果明确的决定，不是缺省。
    /// </summary>
    [Fact]
    public async Task CreateRedemptionCode_ZeroMeansExplicitlyUnlimited()
    {
        var promotion = await SeedPromotionAsync(totalLimit: null, perUserLimit: null, isPublic: false);
        var minted = await InScopeAsync<ICouponIssuanceService, Result<string>>(
            svc => svc.CreateRedemptionCodeAsync(promotion.Id, 5, perUserLimit: 0));

        (await FindRedemptionCodeAsync(minted.Data!)).PerUserLimit.ShouldBeNull();

        var user = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
            (await InScopeAsync<ICouponWalletService, Result<UserCouponDto>>(svc => svc.RedeemAsync(minted.Data!, user))).Succeeded.ShouldBeTrue();
    }

    /// <summary>兑换码是一个可以换钱的不记名凭证：字母表与长度固定，来源是密码学随机数（12 位 32 字符字母表 ≈ 60 bit）。</summary>
    [Fact]
    public void GenerateCode_UsesTheFixedAlphabetAndLength()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var codes = Enumerable.Range(0, 200).Select(_ => RedemptionCode.GenerateCode()).ToList();

        codes.ShouldAllBe(c => c.Length == 12 && c.All(alphabet.Contains));
        codes.Distinct().Count().ShouldBe(codes.Count);
    }

    private async Task<RedemptionCode> FindRedemptionCodeAsync(string code)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<RedemptionCode, Guid>>();
        return (await repo.FirstOrDefaultAsync(r => r.Code == code)).ShouldNotBeNull();
    }

    /// <summary>
    /// 兑换码总量用尽后不能再兑（CAS 生效）
    /// </summary>
    [Fact]
    public async Task Redeem_RespectsTotalQuantity()
    {
        var promotion = await SeedPromotionAsync(totalLimit: null, perUserLimit: null, isPublic: false);
        var code = new RedemptionCode
        {
            Code = "ONLYONE",
            PromotionId = promotion.Id,
            Type = RedemptionCodeType.General,
            Status = RedemptionCodeStatus.Active,
            TotalQuantity = 1,
            RedeemedQuantity = 0,
            ValidFrom = DateTime.UtcNow.AddDays(-1)
        };
        await SeedAsync(code);

        var first = await InScopeAsync<ICouponWalletService, Result<UserCouponDto>>(svc => svc.RedeemAsync("ONLYONE", Guid.NewGuid()));
        var second = await InScopeAsync<ICouponWalletService, Result<UserCouponDto>>(svc => svc.RedeemAsync("ONLYONE", Guid.NewGuid()));

        first.Succeeded.ShouldBeTrue();
        second.Succeeded.ShouldBeFalse();
    }

    /// <summary>
    /// 总量抢不到时不能留下核销记录。
    /// </summary>
    /// <remarks>
    /// <c>ExecuteInUnitOfWorkAsync</c> 只在**抛异常**时回滚，返回失败 Result 照样提交。
    /// 因此配额 CAS 必须先于任何写入完成，否则"配额没抢到"却留下一条使用记录，
    /// 用户白白消耗一次机会，而调用方看到的是失败。
    /// </remarks>
    [Fact]
    public async Task ApplyCoupon_WhenQuotaExhausted_LeavesNoUsageRecord()
    {
        await SeedPromotionAsync(totalLimit: 1, perUserLimit: null);

        var winner = Guid.NewGuid();
        var loser = Guid.NewGuid();

        (await ApplyAsync(winner, "ORDER-WIN")).Succeeded.ShouldBeTrue();

        var rejected = await ApplyAsync(loser, "ORDER-LOSE");
        rejected.Succeeded.ShouldBeFalse();
        rejected.Message.ShouldBe(ErrorCodes.CouponUsageLimitReached);

        // 被拒的用户不该留下任何核销痕迹
        var loserUsages = await InScopeAsync<ICouponWalletService, Result<List<CouponUsageDto>>>(
            svc => svc.GetUserUsedCouponsAsync(loser));
        loserUsages.Data!.ShouldBeEmpty();

        // 计数器仍是 1（没有被"抢不到也加一次"污染）
        (await ReloadPromotionAsync()).UsedCount.ShouldBe(1);
    }

    /// <summary>
    /// "我的券包"只返回自己持有的券与公开促销，不再把全部内部促销列给所有人
    /// </summary>
    [Fact]
    public async Task GetUserAvailableCoupons_ExcludesUnheldPrivatePromotions()
    {
        var privatePromotion = await SeedPromotionAsync(totalLimit: null, perUserLimit: null, isPublic: false);

        var holder = Guid.NewGuid();
        await SeedAsync(new UserCoupon
        {
            UserId = holder,
            PromotionId = privatePromotion.Id,
            Status = UserCouponStatus.Available,
            AcquiredTime = DateTime.UtcNow
        });

        var holderCoupons = await InScopeAsync<ICouponWalletService, Result<List<UserCouponDto>>>(
            svc => svc.GetUserAvailableCouponsAsync(holder));
        var strangerCoupons = await InScopeAsync<ICouponWalletService, Result<List<UserCouponDto>>>(
            svc => svc.GetUserAvailableCouponsAsync(Guid.NewGuid()));

        holderCoupons.Data!.ShouldContain(c => c.Id == privatePromotion.Id && c.IsHeld);
        strangerCoupons.Data!.ShouldNotContain(c => c.Id == privatePromotion.Id);
    }

    /// <summary>
    /// ★ 没装续费包（探针缺席）时，首单预检对<b>所有人</b>答「可用」—— 与核销守卫同一条判据、同一个事实：
    /// 没有订阅表就没有人订阅过。此前预检按「用没用过首单券」另起一套判据，用过一张的人被界面藏掉入口，
    /// 而核销本会放行。
    /// </summary>
    [Fact]
    public async Task CanUseFirstSubscriptionDiscount_WithoutTheProbe_AgreesWithTheValidator_EvenAfterAFirstOrderCoupon()
    {
        var promotion = await SeedPromotionAsync(totalLimit: null, perUserLimit: null, firstSubscriptionOnly: true);
        var userId = Guid.NewGuid();
        await SeedAsync(new CouponUsage
        {
            CouponId = promotion.Id,
            UserId = userId,
            BusinessOrderNo = "ORD-EARLIER",
            DiscountAmount = 10m
        });

        var precheck = await InScopeAsync<ICouponWalletService, Result<bool>>(
            svc => svc.CanUseFirstSubscriptionDiscountAsync(userId));

        precheck.Succeeded.ShouldBeTrue(precheck.Message);
        precheck.Data.ShouldBeTrue("探针缺席时核销守卫不拒绝任何人，预检却把入口藏掉了");
    }

    private async Task<Promotion> ReloadPromotionAsync()
    {
        var promotions = await InScopeAsync<IPromotionService, Result<PromotionDto>>(
            svc => svc.GetByCodeAsync("SAVE10"));

        return new Promotion
        {
            Id = promotions.Data!.Id,
            UsedCount = promotions.Data.UsedCount
        };
    }
}
