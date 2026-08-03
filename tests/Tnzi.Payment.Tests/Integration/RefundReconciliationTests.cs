using Microsoft.Extensions.DependencyInjection;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Providers;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 模拟异步退款的测试渠道：受理时只回"退款中"，随后由对账查询给出终态。
/// 真实渠道（银行卡退回）就是这个形态，常需数日才终结。
/// </summary>
public class AsyncRefundProvider : IPaymentProvider
{
    /// <summary>
    /// 对账查询将返回的终态，由测试设定
    /// </summary>
    public RefundStatus SettledStatus { get; set; } = RefundStatus.Succeeded;

    public string ChannelCode => "AsyncRefund";
    public string ChannelName => "Async Refund (Test)";

    public bool IsSupported(PaymentMethod method) => true;

    public Task<Result<PaymentProviderOrderResult>> CreatePaymentAsync(PaymentProviderCreateDto input)
        => Task.FromResult(Result.Success(new PaymentProviderOrderResult { TradeNo = input.TradeNo }));

    public Task<Result<PaymentProviderQueryResult>> QueryPaymentAsync(string tradeNo)
        => Task.FromResult(Result.Success(new PaymentProviderQueryResult { TradeNo = tradeNo, Status = PaymentStatus.Succeeded }));

    public Task<Result<PaymentProviderQueryResult>> SyncOrderAsync(string tradeNo)
        => QueryPaymentAsync(tradeNo);

    public Task<Result<PaymentProviderRefundResult>> RefundAsync(PaymentProviderRefundDto input)
        => Task.FromResult(Result.Success(new PaymentProviderRefundResult
        {
            RefundNo = input.RefundNo,
            ExternalRefundNo = $"ext_{input.RefundNo}",
            RefundAmount = input.RefundAmount,
            Status = RefundStatus.Refunding
        }));

    public Task<Result<PaymentProviderRefundQueryResult>> QueryRefundAsync(string externalRefundNo)
        => Task.FromResult(Result.Success(new PaymentProviderRefundQueryResult
        {
            RefundNo = externalRefundNo,
            ExternalRefundNo = externalRefundNo,
            Status = SettledStatus,
            CompletedTime = SettledStatus == RefundStatus.Succeeded ? DateTime.UtcNow : null
        }));

    public Task<Result<PaymentProviderCallbackResult>> HandleCallbackAsync(IDictionary<string, string> parameters)
        => Task.FromResult(Result.Failure<PaymentProviderCallbackResult>(ErrorCodes.PaymentChannelNotSupported, 400));

    public Task<bool> VerifySignatureAsync(IDictionary<string, string> parameters) => Task.FromResult(false);

    public Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo)
        => Task.FromResult(Result.Success(new PaymentParamsDto { TradeNo = tradeNo }));
}

/// <summary>
/// 退款对账集成测试。
/// </summary>
/// <remarks>
/// 此前 ProcessRefundInternalAsync 只看 Result 包裹层的成功与否，忽略渠道回报的真实状态，
/// 会把一笔可能数日后才失败的退款当场记成"已成功"，并连带把支付回写成已退款。
/// </remarks>
public class RefundReconciliationTests : PaymentIntegrationTestBase
{
    private readonly AsyncRefundProvider _asyncProvider = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton(_asyncProvider);
        services.AddScoped<IPaymentProvider>(sp => sp.GetRequiredService<AsyncRefundProvider>());
        services.Configure<PaymentOptions>(o =>
        {
            o.Channels["AsyncRefund"] = new ChannelOptions { Enabled = true, Currency = "USD" };
        });
    }

    private async Task<Guid> SeedSucceededPaymentAsync(string tradeNo, decimal paidAmount)
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
            ChannelCode = "AsyncRefund",
            PaymentMethod = PaymentMethod.CreditCard,
            PaidTime = DateTime.UtcNow
        };
        await SeedAsync(payment);
        return payment.Id;
    }

    [Fact]
    public async Task PendingRefund_StaysRefunding_AndDoesNotWriteBackPaymentStatus()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-ASYNC-1", 100m);

        var created = await InScopeAsync<IRefundService, Result<RefundDto>>(
            svc => svc.CreateRefundAsync(new CreateRefundDto { TradeNo = "PAY-ASYNC-1", RefundAmount = 100m, Reason = "async" }));

        created.Succeeded.ShouldBeTrue();

        var refund = await LoadRefundAsync("PAY-ASYNC-1");
        refund.Status.ShouldBe(RefundStatus.Refunding);
        refund.CompletedTime.ShouldBeNull();

        // 退款尚未终结，支付不应被改写成已退款
        (await ReloadAsync<PaymentEntity>(paymentId))!.Status.ShouldBe(PaymentStatus.Succeeded);
    }

    [Fact]
    public async Task Reconcile_SettlesSucceededRefund_AndWritesBackPaymentStatus()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-ASYNC-2", 100m);
        await InScopeAsync<IRefundService, Result<RefundDto>>(
            svc => svc.CreateRefundAsync(new CreateRefundDto { TradeNo = "PAY-ASYNC-2", RefundAmount = 100m, Reason = "async" }));

        _asyncProvider.SettledStatus = RefundStatus.Succeeded;

        var reconciled = await InScopeAsync<IRefundService, Result<int>>(
            svc => svc.ReconcilePendingRefundsAsync());

        reconciled.Data.ShouldBe(1);

        var refund = await LoadRefundAsync("PAY-ASYNC-2");
        refund.Status.ShouldBe(RefundStatus.Succeeded);
        refund.CompletedTime.ShouldNotBeNull();

        (await ReloadAsync<PaymentEntity>(paymentId))!.Status.ShouldBe(PaymentStatus.Refunded);
    }

    /// <summary>
    /// 渠道最终判失败时，退款要落 Failed，且支付保持"已成功"——钱并没有退回去
    /// </summary>
    [Fact]
    public async Task Reconcile_SettlesFailedRefund_AndLeavesPaymentSucceeded()
    {
        var paymentId = await SeedSucceededPaymentAsync("PAY-ASYNC-3", 100m);
        await InScopeAsync<IRefundService, Result<RefundDto>>(
            svc => svc.CreateRefundAsync(new CreateRefundDto { TradeNo = "PAY-ASYNC-3", RefundAmount = 100m, Reason = "async" }));

        _asyncProvider.SettledStatus = RefundStatus.Failed;

        var reconciled = await InScopeAsync<IRefundService, Result<int>>(
            svc => svc.ReconcilePendingRefundsAsync());

        reconciled.Data.ShouldBe(1);

        var refund = await LoadRefundAsync("PAY-ASYNC-3");
        refund.Status.ShouldBe(RefundStatus.Failed);

        (await ReloadAsync<PaymentEntity>(paymentId))!.Status.ShouldBe(PaymentStatus.Succeeded);
    }

    private async Task<Refund> LoadRefundAsync(string tradeNo)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<Tnzi.Domain.Repositories.IRepository<Refund, Guid>>();
        return (await repo.FirstOrDefaultAsync(r => r.Payment!.TradeNo == tradeNo))!;
    }
}
