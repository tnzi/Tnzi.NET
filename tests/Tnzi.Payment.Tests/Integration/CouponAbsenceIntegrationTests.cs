using Microsoft.Extensions.DependencyInjection;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 没装折扣包时，建单路径上的优惠券行为（跑在真实 SQLite + 真实 DI 上）。
/// </summary>
/// <remarks>
/// <para>
/// 这套集成库里<b>没有</b>促销的四张表，容器里也<b>没有</b> <c>ICouponService</c> ——
/// 那不是 mock 出来的现场，就是「只加载支付核心」的宿主本身。
/// </para>
/// <para>
/// ★ 带券码的建单回 <b>400 <c>COUPON_INVALID</c></b> 而不是 501，是刻意的：
/// 501 的语义是「本服务器不提供此功能」，适合一个整体只讲促销的端点；
/// 而这里是<b>建单</b>端点，它本身好端端的，只是收到了一个在这台宿主上不可能有效的券码。
/// 服务端另记一条 Error 指名要加载哪个包 —— 否则一次部署疏漏会被读成「用户老是输错码」，
/// 而两者在客户端看起来完全一样。
/// </para>
/// <para>
/// ★ 更重要的是它<b>不能静默按原价下单</b>：那会让用户以为自己用上了券，
/// 直到看见账单才发现没打折。
/// </para>
/// </remarks>
public class CouponAbsenceIntegrationTests : PaymentIntegrationTestBase
{
    private static CreatePaymentDto NewOrder(string orderNo, string? couponCode = null) => new()
    {
        BusinessOrderNo = orderNo,
        BusinessType = BusinessType.Order,
        Amount = 100m,
        Currency = "USD",
        ChannelCode = "Null",
        CouponCode = couponCode,
        Description = "Absence test order"
    };

    /// <summary>容器里确实没有 <see cref="ICouponService"/> —— 这条是下面两条的前提。</summary>
    [Fact]
    public void TheContainerHasNoCouponService()
    {
        using var scope = ServiceProvider.CreateScope();

        scope.ServiceProvider.GetService<ICouponService>().ShouldBeNull(
            "没装折扣包的宿主里不该有任何 ICouponService 实现 —— 有的话这组用例测的就不是缺席现场。");
    }

    /// <summary>不带券码的建单一个字节不差。</summary>
    [Fact]
    public async Task CreatePayment_WithoutACouponCode_StillWorks()
    {
        var created = await InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(
            svc => svc.CreatePaymentAsync(NewOrder("ORDER-NOCOUPON")));

        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Amount.ShouldBe(100m);
        created.Data.DiscountAmount.ShouldBe(0m);
        created.Data.AppliedCouponCode.ShouldBeNull();
    }

    /// <summary>
    /// 带券码的建单<b>被拒</b>（400 <c>COUPON_INVALID</c>），而不是静默按原价下单。
    /// </summary>
    [Fact]
    public async Task CreatePayment_WithACouponCode_IsRefused_NotSilentlyChargedAtFullPrice()
    {
        var created = await InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(
            svc => svc.CreatePaymentAsync(NewOrder("ORDER-WITHCOUPON", couponCode: "TAKE20")));

        created.Succeeded.ShouldBeFalse("静默按原价下单会让用户以为自己用上了券，直到看见账单");
        created.Code.ShouldBe(400);
        created.Message.ShouldBe(ErrorCodes.CouponInvalid);
    }

    /// <summary>
    /// 缺席回的是 400 而<b>不是 503</b>。
    /// </summary>
    /// <remarks>
    /// 503 意味着暂时故障，会让客户端按退避策略重试一件永远不会恢复的事 ——
    /// 而这台宿主上永远不会有任何优惠券码变得有效。
    /// </remarks>
    [Fact]
    public async Task CreatePayment_WithACouponCode_IsNot503()
    {
        var created = await InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(
            svc => svc.CreatePaymentAsync(NewOrder("ORDER-NOT503", couponCode: "TAKE20")));

        created.Code.ShouldNotBe(503);
    }

    /// <summary>
    /// 支付失败 / 过期的清扫路径在没有优惠券服务时照常走完，不抛异常。
    /// </summary>
    /// <remarks>
    /// 还券那一步现在经 <see cref="ICouponService.ReleaseCouponForPaymentAsync"/> 出去，
    /// 缺席时整段跳过（<c>Payment.CouponId</c> 也必然为 null）。
    /// 这里断言的是「少一项能力，不是抛异常」—— 抛出去会让整轮过期清扫停摆，
    /// 而那两条扫描（关过期支付、对账在途退款）是父模块自己的。
    /// </remarks>
    [Fact]
    public async Task ExpiryScan_StillRuns_WithoutACouponService()
    {
        var created = await InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(
            svc => svc.CreatePaymentAsync(NewOrder("ORDER-EXPIRE-NOCOUPON")));
        created.Succeeded.ShouldBeTrue(created.Message);

        using (var scope = ServiceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<PaymentTestDbContext>();
            var entity = ctx.Set<Entities.Payment>().First(p => p.TradeNo == created.Data!.TradeNo);
            entity.ExpireTime = DateTime.UtcNow.AddMinutes(-5);
            await ctx.SaveChangesAsync();
        }

        var closed = await InScopeAsync<IPaymentService, Result<int>>(svc => svc.CloseExpiredPaymentsAsync());

        closed.Succeeded.ShouldBeTrue(closed.Message);
        closed.Data.ShouldBe(1);
    }
}
