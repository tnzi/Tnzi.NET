using Microsoft.EntityFrameworkCore;
using Tnzi.Payment.Options;
using Tnzi.TestBase;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Promotions.Tests.Integration;

/// <summary>
/// 同一张业务单上先后建了两笔带同一张券的支付（换渠道重付、建单重试）：两笔共用一条核销记录，
/// 这条记录只能在<b>没有任何一笔还在用它</b>时归还。
/// </summary>
/// <remarks>
/// 核销的幂等键是（促销 + 用户 + 业务单号），而归还是按支付找的。此前第二笔命中第一笔的核销记录原样返回、
/// 不改它绑定的支付：第二笔付掉之后第一笔过期，按第一笔把券还了，核销记录被删、总用量与每人用量都退回，
/// 第二笔拿到的折扣从此无账，换一张单号即可无限重复。另一形态：第二笔渠道建单失败，按核销记录 Id 归还，
/// 还掉的是第一笔仍在等付款的那条。
/// </remarks>
public class CouponSharedAcrossPaymentsIntegrationTests : PromotionsIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped<IPaymentProvider, RefusingProvider>();
        services.Configure<PaymentOptions>(o =>
            o.Channels[RefusingProvider.Code] = new ChannelOptions { Enabled = true, Currency = "USD" });
    }

    private static Promotion NewPromotion(string code, bool isPublic = true) => new()
    {
        PromotionCode = code,
        Name = code,
        IsActive = true,
        IsPublic = isPublic,
        StartTime = DateTime.UtcNow.AddDays(-1),
        DiscountType = DiscountType.Fixed,
        DiscountValue = 20m,
        Currency = "USD",
        Stackable = true,
        UsedCount = 0
    };

    private Task<Result<PaymentOrderResultDto>> CreateAsync(string orderNo, string couponCode, string channelCode = "Null") =>
        InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(svc => svc.CreatePaymentAsync(new CreatePaymentDto
        {
            BusinessOrderNo = orderNo,
            BusinessType = BusinessType.Order,
            Amount = 100m,
            Currency = "USD",
            ChannelCode = channelCode,
            CouponCode = couponCode,
            Description = "Shared coupon test"
        }));

    private Task<Result> CallbackAsync(string tradeNo, PaymentStatus status) =>
        InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = new Dictionary<string, string>
            {
                ["trade_no"] = tradeNo,
                ["amount"] = "80",
                ["status"] = status.ToString(),
                ["event_id"] = Guid.NewGuid().ToString("N")
            }
        }));

    private async Task ExpireAsync(params string[] tradeNos)
    {
        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PromotionsTestDbContext>();
            foreach (var payment in ctx.Set<PaymentEntity>().Where(p => tradeNos.Contains(p.TradeNo)))
                payment.ExpireTime = DateTime.UtcNow.AddMinutes(-5);
            await ctx.SaveChangesAsync();
        }

        await InScopeAsync<IPaymentService, Result<int>>(svc => svc.CloseExpiredPaymentsAsync());
    }

    private async Task<PaymentEntity> LoadPaymentAsync(string tradeNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        return (await repo.FirstOrDefaultAsync(p => p.TradeNo == tradeNo))!;
    }

    private async Task<List<CouponUsage>> UsagesForAsync(string orderNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<PromotionsTestDbContext>();
        return await ctx.Set<CouponUsage>().AsNoTracking().Where(u => u.BusinessOrderNo == orderNo).ToListAsync();
    }

    private async Task<int> UsedCountAsync(string code)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<PromotionsTestDbContext>();
        return await ctx.Set<Promotion>().Where(p => p.PromotionCode == code).Select(p => p.UsedCount).SingleAsync();
    }

    [Fact]
    public async Task WhenTheSecondPaymentIsPaidAndTheFirstExpires_TheCouponStaysConsumedByTheSecond()
    {
        await SeedAsync(NewPromotion("SHARE1"));
        var first = await CreateAsync("ORDER-SH1", "SHARE1");
        var second = await CreateAsync("ORDER-SH1", "SHARE1");
        first.Succeeded.ShouldBeTrue(first.Message);
        second.Succeeded.ShouldBeTrue(second.Message);
        second.Data!.DiscountAmount.ShouldBe(20m);

        (await CallbackAsync(second.Data.TradeNo, PaymentStatus.Succeeded)).Succeeded.ShouldBeTrue();
        await ExpireAsync(first.Data!.TradeNo);

        (await LoadPaymentAsync(first.Data.TradeNo)).Status.ShouldBe(PaymentStatus.Expired);
        var usage = (await UsagesForAsync("ORDER-SH1")).ShouldHaveSingleItem();
        usage.PaymentId.ShouldBe((await LoadPaymentAsync(second.Data.TradeNo)).Id);
        (await UsedCountAsync("SHARE1")).ShouldBe(1);
    }

    [Fact]
    public async Task WhenTheFirstPaymentIsPaidAndTheSecondExpires_TheCouponStaysConsumedByTheFirst()
    {
        await SeedAsync(NewPromotion("SHARE2"));
        var first = await CreateAsync("ORDER-SH2", "SHARE2");
        var second = await CreateAsync("ORDER-SH2", "SHARE2");

        (await CallbackAsync(first.Data!.TradeNo, PaymentStatus.Succeeded)).Succeeded.ShouldBeTrue();
        await ExpireAsync(second.Data!.TradeNo);

        var usage = (await UsagesForAsync("ORDER-SH2")).ShouldHaveSingleItem();
        usage.PaymentId.ShouldBe((await LoadPaymentAsync(first.Data.TradeNo)).Id);
        (await UsedCountAsync("SHARE2")).ShouldBe(1);
    }

    /// <summary>第二笔渠道建单失败：归还它自己的份额，第一笔仍在等付款，它依赖的那次核销不能被还掉。</summary>
    [Fact]
    public async Task ASecondPaymentRefusedByItsChannel_DoesNotReleaseTheUsageTheFirstStillWaitsOn()
    {
        await SeedAsync(NewPromotion("SHARE3"));
        var first = await CreateAsync("ORDER-SH3", "SHARE3");
        first.Succeeded.ShouldBeTrue(first.Message);

        var refused = await CreateAsync("ORDER-SH3", "SHARE3", RefusingProvider.Code);
        refused.Succeeded.ShouldBeFalse();

        var usage = (await UsagesForAsync("ORDER-SH3")).ShouldHaveSingleItem();
        usage.PaymentId.ShouldBe((await LoadPaymentAsync(first.Data!.TradeNo)).Id);
        (await UsedCountAsync("SHARE3")).ShouldBe(1);

        // 第一笔付掉：这次折扣有账
        (await CallbackAsync(first.Data.TradeNo, PaymentStatus.Succeeded)).Succeeded.ShouldBeTrue();
        (await UsagesForAsync("ORDER-SH3")).ShouldHaveSingleItem();
    }

    /// <summary>两笔都没付成：最后一笔死掉时券才归还，且只还一次。</summary>
    [Fact]
    public async Task WhenBothPaymentsExpire_TheCouponIsReturnedExactlyOnce()
    {
        await SeedAsync(NewPromotion("SHARE4"));
        var first = await CreateAsync("ORDER-SH4", "SHARE4");
        var second = await CreateAsync("ORDER-SH4", "SHARE4");

        await ExpireAsync(first.Data!.TradeNo, second.Data!.TradeNo);

        (await UsagesForAsync("ORDER-SH4")).ShouldBeEmpty();
        (await UsedCountAsync("SHARE4")).ShouldBe(0);
    }

    /// <summary>
    /// 第二笔的支付行还没提交、归还侧看不见它时，核销已经改绑到第二笔：第一笔过期按自己去还，
    /// 找不到绑定在自己身上的记录，什么都不做，而不是把第二笔正在用的那次核销删掉。
    /// </summary>
    [Fact]
    public async Task ASecondApplyOnTheSameOrder_TakesTheUsageOverBeforeTheFirstPaymentCanReleaseIt()
    {
        await SeedAsync(NewPromotion("SHARE6"));
        var first = await CreateAsync("ORDER-SH6", "SHARE6");
        first.Succeeded.ShouldBeTrue(first.Message);

        var invisibleSecondPayment = Guid.NewGuid();
        var reapplied = await InScopeAsync<ICouponService, Result<CouponUsageDto>>(svc => svc.ApplyCouponAsync(new CouponApplyContext
        {
            CouponCode = "SHARE6",
            UserId = TestHelper.DefaultTestUserId,
            BusinessOrderNo = "ORDER-SH6",
            OrderAmount = 100m,
            Currency = "USD",
            ProductType = ProductType.OneTime,
            PaymentId = invisibleSecondPayment
        }));
        reapplied.Succeeded.ShouldBeTrue(reapplied.Message);

        await ExpireAsync(first.Data!.TradeNo);

        var usage = (await UsagesForAsync("ORDER-SH6")).ShouldHaveSingleItem();
        usage.PaymentId.ShouldBe(invisibleSecondPayment);
        (await UsedCountAsync("SHARE6")).ShouldBe(1);
    }

    /// <summary>手动关单与失败、过期一样是「钱没收到」的终态：券要还回去（同一张单上没有别的支付在用它时）。</summary>
    [Fact]
    public async Task ClosingAPayment_ReturnsItsCoupon()
    {
        await SeedAsync(NewPromotion("SHARE7"));
        var created = await CreateAsync("ORDER-SH7", "SHARE7");
        (await UsedCountAsync("SHARE7")).ShouldBe(1);

        (await InScopeAsync<IPaymentService, Result>(svc => svc.ClosePaymentAsync(created.Data!.TradeNo, "changed my mind"))).Succeeded.ShouldBeTrue();

        (await UsagesForAsync("ORDER-SH7")).ShouldBeEmpty();
        (await UsedCountAsync("SHARE7")).ShouldBe(0);
    }

    [Fact]
    public async Task ClosingTheFirstPayment_WhileTheSecondStillUsesTheCoupon_KeepsIt()
    {
        await SeedAsync(NewPromotion("SHARE8"));
        var first = await CreateAsync("ORDER-SH8", "SHARE8");
        var second = await CreateAsync("ORDER-SH8", "SHARE8");

        (await InScopeAsync<IPaymentService, Result>(svc => svc.ClosePaymentAsync(first.Data!.TradeNo, "switching channel"))).Succeeded.ShouldBeTrue();

        var usage = (await UsagesForAsync("ORDER-SH8")).ShouldHaveSingleItem();
        usage.PaymentId.ShouldBe((await LoadPaymentAsync(second.Data!.TradeNo)).Id);
        (await UsedCountAsync("SHARE8")).ShouldBe(1);
    }

    /// <summary>第一笔先失败（券已还），再建第二笔：第二笔正常核销并绑定到自己（回归）。</summary>
    [Fact]
    public async Task AfterTheFirstPaymentFailed_TheSecondPaymentAppliesItsOwnUsage()
    {
        await SeedAsync(NewPromotion("SHARE5"));
        var first = await CreateAsync("ORDER-SH5", "SHARE5");
        (await CallbackAsync(first.Data!.TradeNo, PaymentStatus.Failed)).Succeeded.ShouldBeTrue();
        (await UsedCountAsync("SHARE5")).ShouldBe(0);

        var second = await CreateAsync("ORDER-SH5", "SHARE5");
        second.Succeeded.ShouldBeTrue(second.Message);

        var usage = (await UsagesForAsync("ORDER-SH5")).ShouldHaveSingleItem();
        usage.PaymentId.ShouldBe((await LoadPaymentAsync(second.Data!.TradeNo)).Id);
        (await UsedCountAsync("SHARE5")).ShouldBe(1);
    }

    /// <summary>
    /// 非公开促销的持券：手上两张券，同一张单先后两笔支付共用一次核销（第二笔的试算凭第二张券通过）。
    /// 第二笔付掉、第一笔过期之后，被核销的那张券不能退回券包，否则这次折扣之后用户手上还是两张可用券。
    /// </summary>
    [Fact]
    public async Task AHeldCoupon_IsNotReturnedToTheWalletWhileAnotherPaymentStillUsesIt()
    {
        var promotion = NewPromotion("HELD1", isPublic: false);
        await SeedAsync(promotion);
        await SeedAsync(
            new UserCoupon { UserId = TestHelper.DefaultTestUserId, PromotionId = promotion.Id, Status = UserCouponStatus.Available, AcquiredTime = DateTime.UtcNow.AddDays(-2) },
            new UserCoupon { UserId = TestHelper.DefaultTestUserId, PromotionId = promotion.Id, Status = UserCouponStatus.Available, AcquiredTime = DateTime.UtcNow.AddDays(-1) });

        var first = await CreateAsync("ORDER-HELD1", "HELD1");
        var second = await CreateAsync("ORDER-HELD1", "HELD1");
        first.Succeeded.ShouldBeTrue(first.Message);
        second.Succeeded.ShouldBeTrue(second.Message);

        (await CallbackAsync(second.Data!.TradeNo, PaymentStatus.Succeeded)).Succeeded.ShouldBeTrue();
        await ExpireAsync(first.Data!.TradeNo);

        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<PromotionsTestDbContext>();
        var held = await ctx.Set<UserCoupon>().AsNoTracking().Where(u => u.PromotionId == promotion.Id).ToListAsync();
        held.Count(u => u.Status == UserCouponStatus.Used).ShouldBe(1);
        held.Count(u => u.Status == UserCouponStatus.Available).ShouldBe(1);
    }

    /// <summary>渠道建单一律失败的测试渠道。</summary>
    private sealed class RefusingProvider : IPaymentProvider
    {
        public const string Code = "Refusing";

        public string ChannelCode => Code;
        public string ChannelName => "Refusing (test)";
        public bool IsSupported(PaymentMethod method) => true;

        public Task<Result<PaymentProviderOrderResult>> CreatePaymentAsync(PaymentProviderCreateDto input) =>
            Task.FromResult(Result.Failure<PaymentProviderOrderResult>("channel refused the order", 400));

        public Task<Result<PaymentProviderQueryResult>> SyncOrderAsync(string tradeNo) => throw new NotSupportedException();
        public Task<Result<PaymentProviderCallbackResult>> HandleCallbackAsync(IDictionary<string, string> parameters) => throw new NotSupportedException();
        public Task<bool> VerifySignatureAsync(IDictionary<string, string> parameters) => Task.FromResult(false);
        public Task<Result<PaymentProviderRefundResult>> RefundAsync(PaymentProviderRefundDto input) => throw new NotSupportedException();
        public Task<Result<PaymentProviderRefundQueryResult>> QueryRefundAsync(string refundNo) => throw new NotSupportedException();
        public Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo) => throw new NotSupportedException();
    }
}
