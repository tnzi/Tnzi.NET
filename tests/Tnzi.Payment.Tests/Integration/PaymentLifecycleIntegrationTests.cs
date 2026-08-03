using Microsoft.Extensions.DependencyInjection;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using Tnzi.TestBase;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 支付生命周期集成测试（真实 SQLite）：折扣与税额落地、关闭的原子性、线下手工确认收款、过期清扫。
/// </summary>
public class PaymentLifecycleIntegrationTests : PaymentIntegrationTestBase
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
        var repo = scope.ServiceProvider.GetRequiredService<Tnzi.Domain.Repositories.IRepository<PaymentEntity, Guid>>();
        return (await repo.FirstOrDefaultAsync(p => p.TradeNo == tradeNo))!;
    }

    [Fact]
    public async Task CreatePayment_WithoutTaxOrCoupon_PayableEqualsAmount()
    {
        var created = await CreateAsync(NewOrder());

        created.Succeeded.ShouldBeTrue();
        created.Data!.Amount.ShouldBe(100m);
        created.Data.DiscountAmount.ShouldBe(0m);
        created.Data.TaxAmount.ShouldBe(0m);

        var payment = await LoadPaymentAsync(created.Data.TradeNo);
        payment.PayableAmount.ShouldBe(100m);
        payment.OriginalAmount.ShouldBe(100m);
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

        var usages = await InScopeAsync<ICouponService, Result<List<CouponUsageDto>>>(
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

    [Fact]
    public async Task ClosePayment_WithPendingPayment_Succeeds()
    {
        var created = await CreateAsync(NewOrder("ORDER-CLOSE"));
        var tradeNo = created.Data!.TradeNo;

        var closed = await InScopeAsync<IPaymentService, Result>(svc => svc.ClosePaymentAsync(tradeNo, "test"));

        closed.Succeeded.ShouldBeTrue();
        (await LoadPaymentAsync(tradeNo)).Status.ShouldBe(PaymentStatus.Closed);
    }

    /// <summary>
    /// 二次关闭走 CAS 落空，返回冲突而不是把已关闭的单再关一次
    /// </summary>
    [Fact]
    public async Task ClosePayment_Twice_SecondFails()
    {
        var created = await CreateAsync(NewOrder("ORDER-CLOSE2"));
        var tradeNo = created.Data!.TradeNo;

        var first = await InScopeAsync<IPaymentService, Result>(svc => svc.ClosePaymentAsync(tradeNo, "test"));
        var second = await InScopeAsync<IPaymentService, Result>(svc => svc.ClosePaymentAsync(tradeNo, "test"));

        first.Succeeded.ShouldBeTrue();
        second.Succeeded.ShouldBeFalse();
    }

    /// <summary>
    /// 线下渠道建单后保持待确认，人工确认收款后才置成功
    /// </summary>
    [Fact]
    public async Task ConfirmOfflinePayment_MarksPaidAndKeepsReference()
    {
        var created = await CreateAsync(new CreatePaymentDto
        {
            BusinessOrderNo = "ORDER-OFFLINE",
            BusinessType = BusinessType.Order,
            Amount = 250m,
            Currency = "USD",
            ChannelCode = PaymentConstants.OfflineChannelCode,
            PaymentMethod = PaymentMethod.BankTransfer
        });

        created.Succeeded.ShouldBeTrue();
        var tradeNo = created.Data!.TradeNo;
        (await LoadPaymentAsync(tradeNo)).Status.ShouldBe(PaymentStatus.Pending);

        var confirmed = await InScopeAsync<IPaymentService, Result<PaymentDto>>(
            svc => svc.ConfirmOfflinePaymentAsync(tradeNo, new ConfirmOfflinePaymentDto
            {
                Reference = "WIRE-88231",
                Remark = "Received via bank transfer"
            }));

        confirmed.Succeeded.ShouldBeTrue();

        var payment = await LoadPaymentAsync(tradeNo);
        payment.Status.ShouldBe(PaymentStatus.Succeeded);
        payment.PaidAmount.ShouldBe(250m);
        payment.ExternalTradeNo.ShouldBe("WIRE-88231");
    }

    /// <summary>
    /// 线下支付的有效期按天计，不能套用在线渠道的分钟级过期。
    /// </summary>
    /// <remarks>
    /// 银行转账 / 汇款要几天才到账，用 30 分钟的默认过期会在钱到账之前就把订单关掉，
    /// 运营随后再也无法确认这笔收款。
    /// </remarks>
    [Fact]
    public async Task OfflinePayment_GetsDayScaleValidity_NotTheOnlineMinuteWindow()
    {
        var created = await CreateAsync(new CreatePaymentDto
        {
            BusinessOrderNo = "ORDER-OFFLINE-TTL",
            BusinessType = BusinessType.Order,
            Amount = 500m,
            Currency = "USD",
            ChannelCode = PaymentConstants.OfflineChannelCode,
            PaymentMethod = PaymentMethod.BankTransfer
        });

        created.Succeeded.ShouldBeTrue();

        var payment = await LoadPaymentAsync(created.Data!.TradeNo);
        // 默认 7 天，远大于在线渠道的 30 分钟
        (payment.ExpireTime!.Value - DateTime.UtcNow).TotalDays.ShouldBeGreaterThan(6);

        // 过期清扫不该碰它
        (await InScopeAsync<IPaymentService, Result<int>>(svc => svc.CloseExpiredPaymentsAsync())).Data.ShouldBe(0);
        (await LoadPaymentAsync(created.Data.TradeNo)).Status.ShouldBe(PaymentStatus.Pending);
    }

    /// <summary>
    /// 手工确认只对线下渠道开放：在线渠道必须以渠道回调为准，
    /// 否则运营就能在无真实收款的情况下把订单标记为已付。
    /// </summary>
    [Fact]
    public async Task ConfirmOfflinePayment_OnOnlineChannel_IsRejected()
    {
        var created = await CreateAsync(NewOrder("ORDER-ONLINE"));

        var confirmed = await InScopeAsync<IPaymentService, Result<PaymentDto>>(
            svc => svc.ConfirmOfflinePaymentAsync(created.Data!.TradeNo, new ConfirmOfflinePaymentDto
            {
                Reference = "FAKE-REF"
            }));

        confirmed.Succeeded.ShouldBeFalse();
        confirmed.Message.ShouldBe(ErrorCodes.PaymentManualConfirmChannelOnly);
    }

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
            var ctx = scope.ServiceProvider.GetRequiredService<PaymentTestDbContext>();
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
