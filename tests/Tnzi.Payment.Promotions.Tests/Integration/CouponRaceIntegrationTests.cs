
namespace Tnzi.Payment.Promotions.Tests.Integration;

/// <summary>
/// 核销事务内部那两道守卫 —— 它们只在**并发**下才会被触发。
/// </summary>
/// <remarks>
/// <para>
/// <c>ApplyCouponAsync</c> 先在事务外做一次校验（那次校验也服务于收银台的试算，
/// 要报出「你已经用过这张券了」这类可读的原因），再在事务内做一次。
/// 顺序执行时事务外那次总是先拒绝，事务内那两道守卫**一次都走不到** ——
/// 于是「删掉它们，全部用例照样绿」，而它们防的正是真实发生的资损。
/// </para>
/// <para>
/// 本类用一个装饰器把那个竞态**确定性地**造出来：校验照常跑真实实现，
/// 返回之前把这张券在库里改掉。那正是「另一笔并发核销在我校验完之后、写入之前提交了」
/// 的状态，不需要真的开两个线程（测试库是单条共享 SQLite 连接，开线程只会撞连接）。
/// </para>
/// </remarks>
public class CouponRaceIntegrationTests : PromotionsIntegrationTestBase
{
    /// <summary>校验通过之后、核销事务开始之前，模拟另一笔并发核销已经提交。</summary>
    private static Func<PromotionsTestDbContext, Task>? _afterValidation;

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // 只替换 ValidateCouponAsync 一个方法：CouponService 只调它，
        // 手写一个完整的装饰器会把 IPromotionService 的全部成员钉进测试里，
        // 接口一动这组用例就因为无关原因红。
        services.AddScoped<PromotionService>();
        services.AddScoped<IPromotionService>(sp =>
        {
            var real = sp.GetRequiredService<PromotionService>();
            var db = sp.GetRequiredService<PromotionsTestDbContext>();
            var mock = new Mock<IPromotionService>();

            mock.Setup(x => x.ValidateCouponAsync(It.IsAny<CouponApplyContext>(), It.IsAny<CancellationToken>()))
                .Returns(async (CouponApplyContext context, CancellationToken ct) =>
                {
                    var result = await real.ValidateCouponAsync(context, ct);

                    var interleave = _afterValidation;
                    _afterValidation = null;
                    if (interleave != null)
                        await interleave(db);

                    return result;
                });

            return mock.Object;
        });
    }

    private async Task<Promotion> SeedPromotionAsync(bool isPublic, int? perUserLimit = null)
    {
        var promotion = new Promotion
        {
            PromotionCode = "RACE10",
            Name = "Race 10%",
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

    private Task<Result<CouponUsageDto>> ApplyAsync(Guid userId, string orderNo) =>
        InScopeAsync<ICouponService, Result<CouponUsageDto>>(svc => svc.ApplyCouponAsync(new CouponApplyContext
        {
            CouponCode = "RACE10",
            UserId = userId,
            BusinessOrderNo = orderNo,
            OrderAmount = 100m,
            Currency = "USD",
            ProductType = ProductType.All
        }));

    /// <summary>
    /// 校验时这张券还在，写入时已经被别人抢走。此前这里**不失败** ——
    /// 照样写一条券 Id 为空的核销记录，于是一张券换来两份折扣，
    /// 账面上却只有一张券被消耗，事后连多用了几次都数不出来。
    /// </summary>
    [Fact]
    public async Task WhenTheHeldCouponIsTakenBetweenValidationAndWrite_TheApplicationIsRefused()
    {
        var user = Guid.NewGuid();
        var promotion = await SeedPromotionAsync(isPublic: false);

        var coupon = new UserCoupon
        {
            UserId = user,
            PromotionId = promotion.Id,
            Status = UserCouponStatus.Available,
            AcquiredTime = DateTime.UtcNow
        };
        await SeedAsync(coupon);

        _afterValidation = async db =>
        {
            var row = db.Set<UserCoupon>().First(u => u.Id == coupon.Id);
            row.Status = UserCouponStatus.Used;
            row.UsedTime = DateTime.UtcNow;
            await db.SaveChangesAsync();
        };

        var applied = await ApplyAsync(user, "ORDER-RACE-1");

        applied.Succeeded.ShouldBeFalse();
        applied.Message.ShouldBe(ErrorCodes.CouponNotHeld);
        // 被拒的那一次不能烧掉总用量名额
        (await ReloadAsync<Promotion>(promotion.Id))!.UsedCount.ShouldBe(0);
    }

    /// <summary>
    /// 校验时这个用户还没用过，写入时另一笔已经提交。每用户次数的判定必须在事务内、
    /// 在总量递增之后（那一刻本事务持有促销行的锁）再读一次，否则并发的每一笔都放行。
    /// </summary>
    [Fact]
    public async Task WhenTheUserLimitIsReachedBetweenValidationAndWrite_TheApplicationIsRefused()
    {
        var user = Guid.NewGuid();
        var promotion = await SeedPromotionAsync(isPublic: true, perUserLimit: 1);

        _afterValidation = async db =>
        {
            db.Set<CouponUsage>().Add(new CouponUsage
            {
                CouponId = promotion.Id,
                UserId = user,
                BusinessOrderNo = "ORDER-RACE-OTHER",
                DiscountAmount = 10m
            });
            await db.SaveChangesAsync();
        };

        var applied = await ApplyAsync(user, "ORDER-RACE-2");

        applied.Succeeded.ShouldBeFalse();
        applied.Message.ShouldBe(ErrorCodes.CouponUsageLimitReached);
        (await ReloadAsync<Promotion>(promotion.Id))!.UsedCount.ShouldBe(0);
    }

    /// <summary>
    /// 同样的竞态发生在**公开**促销上时不该拒绝：那种促销本来就不要求持券，
    /// 收得太紧会把「输码即用」整条堵死。
    /// </summary>
    [Fact]
    public async Task ThatSameRaceOnAPublicPromotion_StillApplies()
    {
        var user = Guid.NewGuid();
        var promotion = await SeedPromotionAsync(isPublic: true);

        var coupon = new UserCoupon
        {
            UserId = user,
            PromotionId = promotion.Id,
            Status = UserCouponStatus.Available,
            AcquiredTime = DateTime.UtcNow
        };
        await SeedAsync(coupon);

        _afterValidation = async db =>
        {
            var row = db.Set<UserCoupon>().First(u => u.Id == coupon.Id);
            row.Status = UserCouponStatus.Used;
            await db.SaveChangesAsync();
        };

        var applied = await ApplyAsync(user, "ORDER-RACE-3");

        applied.Succeeded.ShouldBeTrue();
        (await ReloadAsync<CouponUsage>(applied.Data!.Id))!.UserCouponId.ShouldBeNull();
    }
}
