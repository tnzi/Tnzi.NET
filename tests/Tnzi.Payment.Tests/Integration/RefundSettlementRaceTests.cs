using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.EventBus;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Events;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Providers;
using Tnzi.Payment.Services;
using Tnzi.Results;
using Tnzi.Security.Claims;
using Tnzi.TestBase;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 退款状态的推进都是条件写：审批只推进仍待审的退款，落定只推进仍在途的退款，
/// 在途退款的对账扫描不会被一整页落不定的退款挡住。
/// </summary>
/// <remarks>
/// 此前审批与落定都是「读出、改、整行写回」：取消在审批读写之间落地时，审批把已取消的退款改回「已批准」并执行渠道退款；
/// 两个实例对账同一笔退款时各发一次 <see cref="RefundProcessedEvent"/>。对账扫描每轮只取最老的 200 条，
/// 渠道仍在处理的退款占满这一页时，之后发起的退款永远对不上账。
/// </remarks>
public class RefundSettlementRaceTests : PaymentIntegrationTestBase
{
    private readonly AsyncRefundProvider _asyncProvider = new();
    private readonly RefundEventRecorder _events = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private Action? _onNextUserIdRead;

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton(_asyncProvider);
        services.AddScoped<IPaymentProvider>(sp => sp.GetRequiredService<AsyncRefundProvider>());
        services.Configure<PaymentOptions>(o =>
            o.Channels["AsyncRefund"] = new ChannelOptions { Enabled = true, Currency = "USD" });

        services.AddSingleton(_events);
        services.AddScoped<IEventHandler<RefundProcessedEvent>>(sp => sp.GetRequiredService<RefundEventRecorder>());

        // 审批人身份在「读出待审退款」与「写下审批结果」之间读取：在这一刻插进一次并发取消
        _currentUser.Setup(u => u.Id).Returns(() =>
        {
            Interlocked.Exchange(ref _onNextUserIdRead, null)?.Invoke();
            return TestHelper.DefaultTestUserId;
        });
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        services.AddScoped(_ => _currentUser.Object);
    }

    private async Task<PaymentEntity> SeedPaidAsync(string tradeNo, string channel, decimal amount = 1000m)
    {
        var payment = new PaymentEntity
        {
            TradeNo = tradeNo,
            BusinessOrderNo = "ORDER-" + tradeNo,
            BusinessType = BusinessType.Order,
            OriginalAmount = amount,
            PayableAmount = amount,
            PaidAmount = amount,
            Currency = "USD",
            Status = PaymentStatus.Succeeded,
            ChannelCode = channel,
            PaymentMethod = PaymentMethod.CreditCard,
            PaidTime = DateTime.UtcNow
        };
        await SeedAsync(payment);
        return payment;
    }

    private static Refund NewRefund(PaymentEntity payment, RefundStatus status, string? externalRefundNo = null, DateTime? creationTime = null) => new()
    {
        RefundNo = Refund.GenerateRefundNo(),
        PaymentId = payment.Id,
        RefundAmount = 1m,
        Reason = "test",
        Status = status,
        ExternalRefundNo = externalRefundNo,
        BusinessOrderNo = payment.BusinessOrderNo,
        CreationTime = creationTime ?? default
    };

    private void ChangeStatusOutOfBand(Guid refundId, RefundStatus status)
    {
        using var scope = ServiceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<PaymentTestDbContext>();
        ctx.Set<Refund>().Where(r => r.Id == refundId).ExecuteUpdate(s => s.SetProperty(r => r.Status, status));
    }

    private Task<Result<int>> ReconcileAsync() =>
        InScopeAsync<IRefundService, Result<int>>(svc => svc.ReconcilePendingRefundsAsync());

    // ---- 审批 ----

    [Fact]
    public async Task ApprovingARefundThatIsCancelledMeanwhile_IsRefusedAndNothingIsRefunded()
    {
        var payment = await SeedPaidAsync("RACE-APPROVE", PaymentConstants.NullChannelCode);
        var refund = NewRefund(payment, RefundStatus.Pending);
        await SeedAsync(refund);
        _onNextUserIdRead = () => ChangeStatusOutOfBand(refund.Id, RefundStatus.Cancelled);

        var approved = await InScopeAsync<IRefundService, Result>(
            svc => svc.ApproveRefundAsync(refund.Id, new ApproveRefundDto { Approved = true }));

        approved.Succeeded.ShouldBeFalse();
        approved.Code.ShouldBe(409);
        (await ReloadAsync<Refund>(refund.Id))!.Status.ShouldBe(RefundStatus.Cancelled);
        _events.For(refund.Id).ShouldBeEmpty();
    }

    [Fact]
    public async Task ApprovingAPendingRefund_StillExecutesIt()
    {
        var payment = await SeedPaidAsync("RACE-APPROVE-OK", PaymentConstants.NullChannelCode);
        var refund = NewRefund(payment, RefundStatus.Pending);
        await SeedAsync(refund);

        var approved = await InScopeAsync<IRefundService, Result>(
            svc => svc.ApproveRefundAsync(refund.Id, new ApproveRefundDto { Approved = true, Remark = "ok" }));

        approved.Succeeded.ShouldBeTrue(approved.Message);
        var settled = (await ReloadAsync<Refund>(refund.Id))!;
        settled.Status.ShouldBe(RefundStatus.Succeeded);
        settled.ApproverId.ShouldBe(TestHelper.DefaultTestUserId);
        settled.ApproveRemark.ShouldBe("ok");
        _events.For(refund.Id).ShouldHaveSingleItem().Succeeded.ShouldBeTrue();
    }

    // ---- 落定 ----

    [Fact]
    public async Task TwoReconcilersSettlingTheSameRefund_AnnounceItOnce()
    {
        var payment = await SeedPaidAsync("RACE-SETTLE", "AsyncRefund");
        var refund = NewRefund(payment, RefundStatus.Refunding, externalRefundNo: "ext_RACE-SETTLE");
        await SeedAsync(refund);
        // 另一个实例在这次渠道往返期间把同一笔退款对账落定
        _asyncProvider.DuringQuery = async () => (await ReconcileAsync()).Data.ShouldBe(1);

        var reconciled = await ReconcileAsync();

        reconciled.Data.ShouldBe(0);
        (await ReloadAsync<Refund>(refund.Id))!.Status.ShouldBe(RefundStatus.Succeeded);
        _events.For(refund.Id).ShouldHaveSingleItem();
    }

    // ---- 对账扫描 ----

    [Fact]
    public async Task Reconcile_ReachesRefundsBehindAFullPageOfInFlightOnes()
    {
        const int scanPageSize = 200;
        var payment = await SeedPaidAsync("RACE-PAGE", "AsyncRefund");
        var oldest = DateTime.UtcNow.AddDays(-2);
        await SeedAsync(Enumerable.Range(0, scanPageSize)
            .Select(i => (object)NewRefund(payment, RefundStatus.Refunding, $"ext_STUCK-{i}", oldest.AddSeconds(i)))
            .ToArray());
        var behind = NewRefund(payment, RefundStatus.Refunding, "ext_BEHIND");
        await SeedAsync(behind);
        _asyncProvider.SettledStatusFor = externalRefundNo => externalRefundNo.StartsWith("ext_STUCK-", StringComparison.Ordinal)
            ? RefundStatus.Refunding
            : RefundStatus.Succeeded;

        (await ReconcileAsync()).Data.ShouldBe(1);

        (await ReloadAsync<Refund>(behind.Id))!.Status.ShouldBe(RefundStatus.Succeeded);
    }

    /// <summary>记下收到的退款落定事件。</summary>
    public sealed class RefundEventRecorder : IEventHandler<RefundProcessedEvent>
    {
        private readonly ConcurrentQueue<RefundProcessedEvent> _received = new();

        public IReadOnlyList<RefundProcessedEvent> For(Guid refundId) => _received.Where(e => e.RefundId == refundId).ToList();

        public Task HandleAsync(RefundProcessedEvent eventData, CancellationToken cancellationToken = default)
        {
            _received.Enqueue(eventData);
            return Task.CompletedTask;
        }
    }
}
