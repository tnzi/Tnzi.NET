using Tnzi.TestBase;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Promotions.Tests.Integration;

/// <summary>
/// 支付 + 折扣<b>同时装上</b>时的建单与清扫链路：券真的影响到渠道收款额、
/// 无效券码让建单失败而不是静默按原价下单、支付过期把券还回去。
/// </summary>
/// <remarks>
/// 这三个用例拆分前住在父测试项目的 <c>PaymentLifecycleIntegrationTests</c> 里。
/// 它们跨两个包，所以留在父项目就跑不起来 —— 父项目刻意不引用本包，跑的是
/// 「没装折扣包」的宿主。同一个文件里那些与折扣无关的用例（关闭、线下确认、
/// 线下有效期、在线渠道拒绝手工确认）留在了那边。
/// </remarks>
public class PaymentWithCouponIntegrationTests : PromotionsIntegrationTestBase
{
    private static CreatePaymentDto NewOrder(string orderNo = "ORDER-1", decimal amount = 100m, string? couponCode = null) => new()
    {
        BusinessOrderNo = orderNo,
        BusinessType = BusinessType.Order,
        Amount = amount,
        Currency = "USD",
        ChannelCode = "Null",
        CouponCode = couponCode,
        Description = "Integration test order"
    };

    private Task<Result<PaymentOrderResultDto>> CreateAsync(CreatePaymentDto request) =>
        InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(svc => svc.CreatePaymentAsync(request));

    private async Task<PaymentEntity> LoadPaymentAsync(string tradeNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        return (await repo.FirstOrDefaultAsync(p => p.TradeNo == tradeNo))!;
    }

    /// <summary>
    /// 优惠券必须真正影响到渠道收款额：此前 CouponCode 被接收后直接丢弃，折扣恒为 0。
    /// </summary>
    [Fact]
    public async Task CreatePayment_WithCoupon_AppliesDiscountAndRecordsUsage()
    {
        await SeedAsync(new Promotion
        {
            PromotionCode = "TAKE20",
            Name = "20 off",
            IsActive = true,
            IsPublic = true,
            StartTime = DateTime.UtcNow.AddDays(-1),
            DiscountType = DiscountType.Fixed,
            DiscountValue = 20m,
            Currency = "USD",
            Stackable = true,
            UsedCount = 0
        });

        var created = await CreateAsync(NewOrder(couponCode: "TAKE20"));

        created.Succeeded.ShouldBeTrue();
        created.Data!.DiscountAmount.ShouldBe(20m);
        created.Data.Amount.ShouldBe(80m);
        created.Data.AppliedCouponCode.ShouldBe("TAKE20");

        var payment = await LoadPaymentAsync(created.Data.TradeNo);
        payment.PayableAmount.ShouldBe(80m);
        payment.DiscountAmount.ShouldBe(20m);
        payment.CouponId.ShouldNotBeNull();

        var usages = await InScopeAsync<ICouponWalletService, Result<List<CouponUsageDto>>>(
            svc => svc.GetUserUsedCouponsAsync(TestHelper.DefaultTestUserId));
        usages.Data!.ShouldContain(u => u.BusinessOrderNo == "ORDER-1" && u.DiscountAmount == 20m);
    }
    /// <summary>
    /// 无效优惠券必须让建单失败，而不是"静默按原价下单"
    /// </summary>
    [Fact]
    public async Task CreatePayment_WithUnknownCoupon_Fails()
    {
        var created = await CreateAsync(NewOrder(couponCode: "NOSUCHCODE"));

        created.Succeeded.ShouldBeFalse();
        created.Message.ShouldBe(ErrorCodes.CouponNotFound);
    }

    /// <summary>
    /// 券不可叠加而拒绝建单时，错误码要穿过支付层原样带给调用方 ——
    /// 只剩消息与状态码，客户端就分不清「这张券不能与已用的券同单」和别的 400。
    /// </summary>
    [Fact]
    public async Task CreatePayment_WhenTheOrderAlreadyCarriesANonStackableCoupon_FailsWithTheStackingErrorCode()
    {
        await SeedAsync(
            NewPromotion("SOLO", stackable: false),
            NewPromotion("EXTRA", stackable: true));

        (await CreateAsync(NewOrder(orderNo: "ORDER-STACK", couponCode: "SOLO"))).Succeeded.ShouldBeTrue();

        var second = await CreateAsync(NewOrder(orderNo: "ORDER-STACK", couponCode: "EXTRA"));

        second.Succeeded.ShouldBeFalse();
        second.ErrorCode.ShouldBe(ErrorCodes.CouponNotStackable);
    }

    private static Promotion NewPromotion(string code, bool stackable) => new()
    {
        PromotionCode = code,
        Name = code,
        IsActive = true,
        IsPublic = true,
        StartTime = DateTime.UtcNow.AddDays(-1),
        DiscountType = DiscountType.Fixed,
        DiscountValue = 5m,
        Currency = "USD",
        Stackable = stackable,
        UsedCount = 0
    };
    /// <summary>
    /// 支付过期时归还已核销的优惠券，否则用户付款没成还白丢一张券
    /// </summary>
    [Fact]
    public async Task ExpirePayment_ReleasesCoupon()
    {
        await SeedAsync(new Promotion
        {
            PromotionCode = "EXPCOUPON",
            Name = "Expiry test",
            IsActive = true,
            IsPublic = true,
            StartTime = DateTime.UtcNow.AddDays(-1),
            DiscountType = DiscountType.Fixed,
            DiscountValue = 10m,
            Currency = "USD",
            Stackable = true,
            UsedCount = 0
        });

        var created = await CreateAsync(NewOrder("ORDER-EXPIRE", couponCode: "EXPCOUPON"));
        created.Succeeded.ShouldBeTrue();

        // 把过期时间拨到过去，模拟超时未支付
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PromotionsTestDbContext>();
            var entity = ctx.Set<PaymentEntity>().First(p => p.TradeNo == created.Data!.TradeNo);
            entity.ExpireTime = DateTime.UtcNow.AddMinutes(-5);
            await ctx.SaveChangesAsync();
        }

        var closed = await InScopeAsync<IPaymentService, Result<int>>(svc => svc.CloseExpiredPaymentsAsync());
        closed.Data.ShouldBe(1);

        (await LoadPaymentAsync(created.Data!.TradeNo)).Status.ShouldBe(PaymentStatus.Expired);

        var promotion = await InScopeAsync<IPromotionService, Result<PromotionDto>>(
            svc => svc.GetByCodeAsync("EXPCOUPON"));
        promotion.Data!.UsedCount.ShouldBe(0);
    }
}
