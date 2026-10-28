using Microsoft.Extensions.DependencyInjection;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 一次被拒绝的退款申请不能在支付行上留下额度占用。
/// </summary>
/// <remarks>
/// 退款创建在事务里先对支付行做额度占用（原子条件自增），后面还有会返回失败的校验。
/// <c>ExecuteInUnitOfWorkAsync</c> 对返回的失败照样提交：校验失败时占用若不撤回，
/// 这笔支付的可退额度就被一次没发生的退款永久吃掉，此后谁来退都是「超出可退金额」。
/// </remarks>
public abstract class RefundCreationGuardTestBase : PaymentIntegrationTestBase
{
    protected async Task<Guid> SeedSucceededPaymentAsync(string tradeNo, decimal paidAmount, Guid? userId = null)
    {
        var payment = new PaymentEntity
        {
            TradeNo = tradeNo,
            BusinessOrderNo = "ORDER-" + tradeNo,
            BusinessType = BusinessType.Order,
            OriginalAmount = paidAmount,
            PaidAmount = paidAmount,
            PayableAmount = paidAmount,
            Currency = "USD",
            Status = PaymentStatus.Succeeded,
            ChannelCode = "Null",
            PaymentMethod = PaymentMethod.CreditCard,
            PaidTime = DateTime.UtcNow,
            UserId = userId,
            BillingAccountType = userId.HasValue ? BillingAccountTypes.User : null,
            BillingAccountId = userId?.ToString()
        };
        await SeedAsync(payment);
        return payment.Id;
    }

    protected Task<Result<RefundDto>> CreateRefundAsync(string tradeNo, decimal amount, Guid? ownerUserId = null) =>
        InScopeAsync<IRefundService, Result<RefundDto>>(svc => svc.CreateRefundAsync(
            new CreateRefundDto { TradeNo = tradeNo, RefundAmount = amount, Reason = "test" }, ownerUserId));
}

/// <summary>当天的退款总额超过上限：拒绝之后这笔支付的占用额必须原样不动。</summary>
public class RefundDailyLimitReservationTests : RefundCreationGuardTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<Options.PaymentOptions>(o => o.MaxRefundAmountPerDay = 50m);
    }

    [Fact]
    public async Task ARefundRefusedByTheDailyLimit_LeavesNoReservationBehind()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-DAILY-1", 100m);

        var refused = await CreateRefundAsync("PAY-DAILY-1", 100m);

        refused.Succeeded.ShouldBeFalse();
        refused.Message.ShouldBe(ErrorCodes.RefundDailyLimitExceeded);
        (await ReloadAsync<PaymentEntity>(paymentId))!.ReservedRefundAmount.ShouldBe(0m);

        // 限额之内的那一部分照样退得出来：可退额度没有被那次被拒的申请占掉
        (await CreateRefundAsync("PAY-DAILY-1", 40m)).Succeeded.ShouldBeTrue();
        (await ReloadAsync<PaymentEntity>(paymentId))!.Status.ShouldBe(PaymentStatus.PartialRefunded);
    }
}

/// <summary>
/// 迁移前就有退款行、占用额从 0 起算的支付：按退款表求和的那道校验拒绝之后，占用额同样不能残留。
/// </summary>
public class RefundLegacySumReservationTests : RefundCreationGuardTestBase
{
    [Fact]
    public async Task ARefundRefusedByTheRefundSum_LeavesNoReservationBehind()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-LEGACY-1", 100m);
        await SeedAsync(new Refund
        {
            RefundNo = Refund.GenerateRefundNo(),
            PaymentId = paymentId,
            RefundAmount = 30m,
            Currency = "USD",
            Reason = "before the reservation column existed",
            RefundType = RefundType.Partial,
            Status = RefundStatus.Succeeded,
            BusinessOrderNo = "ORDER-PAY-LEGACY-1",
            CompletedTime = DateTime.UtcNow
        });

        var refused = await CreateRefundAsync("PAY-LEGACY-1", 80m);

        refused.Succeeded.ShouldBeFalse();
        refused.Message.ShouldBe(ErrorCodes.PaymentRefundExceedAmount);
        (await ReloadAsync<PaymentEntity>(paymentId))!.ReservedRefundAmount.ShouldBe(0m);

        // 真正还剩的 70 照样退得出来
        (await CreateRefundAsync("PAY-LEGACY-1", 70m)).Succeeded.ShouldBeTrue();
    }
}

/// <summary>
/// 付款人自己发起的退款一律进审批，阈值只管客服代客退款；宿主显式关掉审批时才全部当场执行。
/// </summary>
/// <remarks>
/// 默认配置（开启审批、阈值 1000）下，付款人对自己 1000 以下的支付调 <c>POST /refunds</c> 即刻退款到账，
/// 而订阅、开票与业务订单对退款零反应：付费即退、服务照用，每一期都能重来一次。
/// </remarks>
public class UserInitiatedRefundApprovalTests : RefundCreationGuardTestBase
{
    private static readonly Guid Payer = Guid.NewGuid();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<Options.PaymentOptions>(o =>
        {
            o.EnableRefundApproval = true;
            o.RefundApprovalThreshold = 1000m;
        });
    }

    [Fact]
    public async Task APayerRefundingTheirOwnPaymentBelowTheThreshold_WaitsForApproval()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-SELF-1", 20m, Payer);

        var created = await CreateRefundAsync("PAY-SELF-1", 20m, ownerUserId: Payer);

        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Status.ShouldBe(RefundStatus.Pending);
        // 渠道没有被调用：支付仍是已付，没有任何一笔成功退款
        var payment = await ReloadAsync<PaymentEntity>(paymentId);
        payment!.Status.ShouldBe(PaymentStatus.Succeeded);
        payment.ReservedRefundAmount.ShouldBe(20m);
    }

    [Fact]
    public async Task AnAdminRefundBelowTheThreshold_IsStillExecutedAtOnce()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-ADMIN-1", 20m, Payer);

        var created = await CreateRefundAsync("PAY-ADMIN-1", 20m);

        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Status.ShouldBe(RefundStatus.Succeeded);
        (await ReloadAsync<PaymentEntity>(paymentId))!.Status.ShouldBe(PaymentStatus.Refunded);
    }

    [Fact]
    public async Task APayerCannotRefundSomeoneElsesPayment()
    {
        await SeedSucceededPaymentAsync("PAY-OTHER-1", 20m, Guid.NewGuid());

        var refused = await CreateRefundAsync("PAY-OTHER-1", 20m, ownerUserId: Payer);

        refused.Succeeded.ShouldBeFalse();
        refused.Message.ShouldBe(ErrorCodes.PaymentNotFound);
    }
}

/// <summary>宿主显式关掉审批：那是它的选择，付款人发起的退款照旧当场执行。</summary>
public class UserInitiatedRefundWithoutApprovalTests : RefundCreationGuardTestBase
{
    private static readonly Guid Payer = Guid.NewGuid();

    [Fact]
    public async Task WithApprovalTurnedOff_APayerRefundIsExecutedAtOnce()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-SELF-2", 20m, Payer);

        var created = await CreateRefundAsync("PAY-SELF-2", 20m, ownerUserId: Payer);

        created.Succeeded.ShouldBeTrue(created.Message);
        created.Data!.Status.ShouldBe(RefundStatus.Succeeded);
        (await ReloadAsync<PaymentEntity>(paymentId))!.Status.ShouldBe(PaymentStatus.Refunded);
    }
}

/// <summary>
/// 宿主关掉付款方自助退款（按月收服务费的部署）：用户面申请一律 403，运营方代客退款照常。
/// </summary>
public class CustomerRefundRequestsDisabledTests : RefundCreationGuardTestBase
{
    private static readonly Guid Payer = Guid.NewGuid();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<Options.PaymentOptions>(o => o.AllowCustomerRefundRequests = false);
    }

    [Fact]
    public async Task APayerCannotRequestARefund()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-NOSELF-1", 20m, Payer);

        var refused = await CreateRefundAsync("PAY-NOSELF-1", 20m, ownerUserId: Payer);

        refused.Code.ShouldBe(403);
        refused.Message.ShouldBe(ErrorCodes.PaymentRefundRequestsDisabled);
        (await ReloadAsync<PaymentEntity>(paymentId))!.ReservedRefundAmount.ShouldBe(0m, "a refused request reserves nothing");
    }

    [Fact]
    public async Task AnAdministratorCanStillRefund()
    {
        await SeedSucceededPaymentAsync("PAY-NOSELF-2", 20m, Payer);

        var created = await CreateRefundAsync("PAY-NOSELF-2", 20m);

        created.Succeeded.ShouldBeTrue(created.Message);
    }
}
