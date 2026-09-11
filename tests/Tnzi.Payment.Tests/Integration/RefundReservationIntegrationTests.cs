using Tnzi.Payment.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 退款额度的**占用**（reservation）：并发下的超退防线。
/// </summary>
/// <remarks>
/// 超退守卫此前是「先 <c>SumAsync</c> 已有退款、再 <c>InsertAsync</c> 新退款」，
/// 两步之间没有任何互斥：两笔并发的 60 元退款各自读到 sum=0，都判定
/// 「100 - 0 >= 60」通过，落地后累计退了 120。线下渠道当场记成功，那就是真实资损。
///
/// 修法是把额度占用做成支付行上的一次**原子条件更新**
/// （<c>WHERE ReservedRefundAmount + 金额 &lt;= PaidAmount</c>），
/// 与写入同一个事务，于是第二笔必须等第一笔提交后重新求值，看到的是已被占用的额度。
/// </remarks>
public class RefundReservationIntegrationTests : PaymentIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // 打开退款审批：退款停在 Pending，才测得到「拒绝 / 取消之后额度还回去了没有」。
        services.Configure<Options.PaymentOptions>(o =>
        {
            o.EnableRefundApproval = true;
            o.RefundApprovalThreshold = 0m;
        });
    }

    private async Task<Guid> SeedSucceededPaymentAsync(string tradeNo, decimal paidAmount, decimal reserved = 0m)
    {
        var payment = new PaymentEntity
        {
            TradeNo = tradeNo,
            BusinessOrderNo = "ORDER-" + tradeNo,
            BusinessType = BusinessType.Order,
            OriginalAmount = paidAmount,
            PaidAmount = paidAmount,
            ReservedRefundAmount = reserved,
            Currency = "USD",
            Status = PaymentStatus.Succeeded,
            ChannelCode = "Null",
            PaymentMethod = PaymentMethod.CreditCard,
            PaidTime = DateTime.UtcNow
        };
        await SeedAsync(payment);
        return payment.Id;
    }

    private Task<Result<RefundDto>> CreateRefundAsync(string tradeNo, decimal amount) =>
        InScopeAsync<IRefundService, Result<RefundDto>>(
            svc => svc.CreateRefundAsync(new CreateRefundDto { TradeNo = tradeNo, RefundAmount = amount, Reason = "test" }));

    [Fact]
    public async Task CreatingARefund_ReservesTheAmountOnThePaymentRow()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-RES1", 100m);

        (await CreateRefundAsync("PAY-RES1", 60m)).Succeeded.ShouldBeTrue();

        (await ReloadAsync<PaymentEntity>(paymentId))!.ReservedRefundAmount.ShouldBe(60m);
    }

    /// <summary>
    /// 这是并发那一刻的等价构造：额度已被另一笔事务占用，但它的退款行还没提交，
    /// 所以本事务 <c>SumAsync</c> 出来仍是 0。只靠求和的守卫会在这里放行 ——
    /// 那正是两笔各 60 元退掉 120 元的那一步。
    /// </summary>
    [Fact]
    public async Task ARefundThatOnlyFitsBecauseAnotherIsStillUncommitted_IsRejected()
    {
        await SeedSucceededPaymentAsync("PAY-RES2", 100m, reserved: 60m);

        var result = await CreateRefundAsync("PAY-RES2", 60m);

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PaymentRefundExceedAmount);
    }

    [Fact]
    public async Task RefundsThatTogetherFitWithinThePaidAmount_AreBothAccepted()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-RES3", 100m);

        (await CreateRefundAsync("PAY-RES3", 60m)).Succeeded.ShouldBeTrue();
        (await CreateRefundAsync("PAY-RES3", 40m)).Succeeded.ShouldBeTrue();

        (await ReloadAsync<PaymentEntity>(paymentId))!.ReservedRefundAmount.ShouldBe(100m);
    }

    /// <summary>
    /// 被拒绝的退款必须把额度还回去。此前求和的过滤条件只排掉 Cancelled 与 Failed，
    /// <c>Rejected</c> 照样计入 —— 一笔审批被拒的全额退款会把这笔支付**永久**变成不可退。
    /// </summary>
    [Fact]
    public async Task RejectingARefund_ReleasesTheReservedAmount()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-RES4", 100m);

        var created = await CreateRefundAsync("PAY-RES4", 100m);
        created.Succeeded.ShouldBeTrue();
        (await ReloadAsync<PaymentEntity>(paymentId))!.ReservedRefundAmount.ShouldBe(100m);

        var rejected = await InScopeAsync<IRefundService, Result>(svc =>
            svc.ApproveRefundAsync(created.Data!.Id, new ApproveRefundDto { Approved = false, Remark = "no" }));
        rejected.Succeeded.ShouldBeTrue();

        (await ReloadAsync<PaymentEntity>(paymentId))!.ReservedRefundAmount.ShouldBe(0m);

        // 额度还回去了，这笔支付重新可退
        (await CreateRefundAsync("PAY-RES4", 100m)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task CancellingARefund_ReleasesTheReservedAmount()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-RES5", 100m);

        var created = await CreateRefundAsync("PAY-RES5", 100m);
        created.Succeeded.ShouldBeTrue();

        var cancelled = await InScopeAsync<IRefundService, Result>(
            svc => svc.CancelRefundAsync(created.Data!.Id, "changed mind"));
        cancelled.Succeeded.ShouldBeTrue();

        (await ReloadAsync<PaymentEntity>(paymentId))!.ReservedRefundAmount.ShouldBe(0m);
    }

    /// <summary>
    /// 释放是**按仍在占用的退款行重算**而不是「减掉这一笔」，因此它自我纠正：
    /// 无论此前漏还过还是多还过，一次释放之后占用额恒等于实际仍在占用的那些退款之和。
    /// 做减法则会把历史误差一直累积下去 —— 少还的额度永久锁死这笔支付的可退空间，
    /// 多还的额度让它可以退出超过它收到的钱，两个方向都没有任何症状。
    /// </summary>
    [Fact]
    public async Task ReleasingRecomputesFromTheActiveRefunds_HealingAnyDrift()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-RES6", 100m);

        var first = await CreateRefundAsync("PAY-RES6", 40m);
        var second = await CreateRefundAsync("PAY-RES6", 40m);
        first.Succeeded.ShouldBeTrue();
        second.Succeeded.ShouldBeTrue();

        // 制造一次漂移：占用额被记成 100，而实际在占用的只有两笔各 40
        await DriftReservedAmountAsync(paymentId, 100m);

        await InScopeAsync<IRefundService, Result>(svc => svc.CancelRefundAsync(second.Data!.Id, "x"));

        // 重算 → 落在第一笔的 40 上；做减法则会落在 100 - 40 = 60
        (await ReloadAsync<PaymentEntity>(paymentId))!.ReservedRefundAmount.ShouldBe(40m);
    }

    private async Task DriftReservedAmountAsync(Guid paymentId, decimal value)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<PaymentTestDbContext>();
        var payment = ctx.Set<PaymentEntity>().First(p => p.Id == paymentId);
        payment.ReservedRefundAmount = value;
        await ctx.SaveChangesAsync();
    }
}
