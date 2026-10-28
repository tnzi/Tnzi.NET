using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Domain.Repositories;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 收入统计与真实的退款流程对得上：退过款的支付仍是收到过的钱，退款单独扣一次。
/// </summary>
/// <remarks>
/// 退款成功后支付被回写成 <c>PartialRefunded</c> / <c>Refunded</c>。总览与收入趋势此前只认
/// <c>Status == Succeeded</c>：这笔支付整笔从收入里消失，它的退款额却照样被减一次，
/// 100 元退 10 元在净收入里记成 −10 而不是 +90，与按行「实收 − 退款」的对账导出对不上。
/// 单元测试没抓到它，是因为夹具里的退款配的是一笔仍处于 <c>Succeeded</c> 的支付，真实流程里不存在这种组合。
/// </remarks>
public class PaymentStatisticsRefundTests : PaymentIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped<IPaymentStatisticsService, PaymentStatisticsService>();
    }

    private async Task<Guid> SeedPaidAsync(string tradeNo, decimal amount, string channel = "Null")
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
        return payment.Id;
    }

    private async Task RefundAsync(string tradeNo, decimal amount)
    {
        var refunded = await InScopeAsync<IRefundService, Result<RefundDto>>(svc => svc.CreateRefundAsync(
            new CreateRefundDto { TradeNo = tradeNo, RefundAmount = amount, Reason = "test" }));
        refunded.Succeeded.ShouldBeTrue(refunded.Message);
        refunded.Data!.Status.ShouldBe(RefundStatus.Succeeded);
    }

    private async Task MovePaymentCreationAsync(Guid paymentId, DateTime creationTime)
    {
        using var scope = ServiceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
        await repo.AsQueryable()
            .Where(p => p.Id == paymentId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreationTime, creationTime));
    }

    private static StatisticsQueryDto Window() => new()
    {
        StartTime = DateTime.UtcNow.AddDays(-5),
        EndTime = DateTime.UtcNow.AddMinutes(5)
    };

    private static RevenueTrendQueryDto TrendWindow() => new()
    {
        StartTime = DateTime.UtcNow.AddDays(-5),
        EndTime = DateTime.UtcNow.AddMinutes(5),
        Granularity = TrendGranularity.Day
    };

    /// <summary>
    /// 退款分析在数据库里分组汇总（真实 SQLite 上跑，验的是查询能翻译、结果与按行计算一致）：
    /// 空白原因并成 Unspecified，支付查不到（已删除）的退款记为 Unknown 渠道，平均处理时长只算已完成的。
    /// </summary>
    [Fact]
    public async Task RefundAnalytics_AreAggregatedInTheDatabase()
    {
        var stripeId = await SeedPaidAsync("PAY-RA-1", 500m, channel: "Stripe");
        var paypalId = await SeedPaidAsync("PAY-RA-2", 500m, channel: "PayPal");
        var deletedId = await SeedPaidAsync("PAY-RA-3", 500m, channel: "Stripe");
        using (var scope = ServiceProvider.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IRepository<PaymentEntity, Guid>>();
            await repo.AsQueryable()
                .Where(p => p.Id == deletedId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDeleted, true));
        }

        var now = DateTime.UtcNow;
        Refund NewRefund(Guid paymentId, decimal amount, string reason, RefundStatus status, double? completedAfterHours = null) => new()
        {
            RefundNo = Refund.GenerateRefundNo(),
            PaymentId = paymentId,
            RefundAmount = amount,
            Reason = reason,
            Status = status,
            BusinessOrderNo = "ORDER-RA",
            CreationTime = now.AddHours(-48),
            CompletedTime = completedAfterHours is { } h ? now.AddHours(-48 + h) : null
        };

        await SeedAsync(
            NewRefund(stripeId, 50m, "Defective", RefundStatus.Succeeded, completedAfterHours: 24),
            NewRefund(stripeId, 30m, "Defective", RefundStatus.Succeeded, completedAfterHours: 48),
            NewRefund(paypalId, 100m, "", RefundStatus.Pending),
            NewRefund(paypalId, 20m, "   ", RefundStatus.Failed),
            NewRefund(deletedId, 10m, "Changed mind", RefundStatus.Succeeded, completedAfterHours: 36));

        var analytics = (await InScopeAsync<IPaymentStatisticsService, Result<RefundAnalyticsDto>>(
            svc => svc.GetRefundAnalyticsAsync())).Data!;

        analytics.TotalRefundCount.ShouldBe(5);
        analytics.TotalRefundAmount.ShouldBe(210m);
        analytics.AverageProcessingTimeHours.ShouldBe(36d);

        var reasons = analytics.ReasonBreakdown.ToDictionary(r => r.Reason);
        reasons.Keys.ShouldBe(["Defective", "Unspecified", "Changed mind"], ignoreOrder: true);
        reasons["Defective"].Count.ShouldBe(2);
        reasons["Defective"].Amount.ShouldBe(80m);
        reasons["Unspecified"].Count.ShouldBe(2);
        reasons["Unspecified"].Amount.ShouldBe(120m);
        reasons["Unspecified"].Percentage.ShouldBe(40m);

        var channels = analytics.ChannelBreakdown.ToDictionary(c => c.ChannelCode);
        channels.Keys.ShouldBe(["Stripe", "PayPal", "Unknown"], ignoreOrder: true);
        channels["Stripe"].Amount.ShouldBe(80m);
        channels["PayPal"].Count.ShouldBe(2);
        channels["Unknown"].Amount.ShouldBe(10m);

        var statuses = analytics.StatusBreakdown.ToDictionary(s => s.Status);
        statuses[nameof(RefundStatus.Succeeded)].Count.ShouldBe(3);
        statuses[nameof(RefundStatus.Succeeded)].Percentage.ShouldBe(60m);
        analytics.StatusBreakdown[0].Status.ShouldBe(nameof(RefundStatus.Succeeded));
    }

    [Fact]
    public async Task APartiallyRefundedPayment_StaysInRevenueAndTheRefundIsSubtractedOnce()
    {
        await SeedPaidAsync("PAY-STAT-1", 100m);
        await RefundAsync("PAY-STAT-1", 10m);

        var overview = await InScopeAsync<IPaymentStatisticsService, Result<PaymentStatisticsDto>>(
            svc => svc.GetStatisticsAsync(Window()));

        overview.Data!.TotalRevenue.ShouldBe(100m);
        overview.Data.SuccessfulTransactions.ShouldBe(1);
        overview.Data.TotalRefunds.ShouldBe(10m);
        overview.Data.ChannelDistribution.Single().Revenue.ShouldBe(100m);

        var trend = await InScopeAsync<IPaymentStatisticsService, Result<List<RevenueTrendPointDto>>>(
            svc => svc.GetRevenueTrendAsync(TrendWindow()));

        var point = trend.Data!.Single();
        point.Revenue.ShouldBe(100m);
        point.TransactionCount.ShouldBe(1);
        point.RefundAmount.ShouldBe(10m);
        point.NetRevenue.ShouldBe(90m);
    }

    [Fact]
    public async Task AFullyRefundedPayment_NetsToZero()
    {
        await SeedPaidAsync("PAY-STAT-2", 100m);
        await RefundAsync("PAY-STAT-2", 100m);

        var overview = await InScopeAsync<IPaymentStatisticsService, Result<PaymentStatisticsDto>>(
            svc => svc.GetStatisticsAsync(Window()));
        overview.Data!.TotalRevenue.ShouldBe(100m);
        overview.Data.TotalRefunds.ShouldBe(100m);

        var trend = await InScopeAsync<IPaymentStatisticsService, Result<List<RevenueTrendPointDto>>>(
            svc => svc.GetRevenueTrendAsync(TrendWindow()));
        trend.Data!.Single().NetRevenue.ShouldBe(0m);
    }

    /// <summary>
    /// 退款落在一个没有任何成功支付的日子：那一天照样出现在趋势里（收入 0、净收入为负），
    /// 而不是连同它的退款一起从趋势与按周 / 按月汇总里消失。
    /// </summary>
    [Fact]
    public async Task ARefundOnADayWithoutPayments_StillAppearsInTheTrend()
    {
        var paymentId = await SeedPaidAsync("PAY-STAT-3", 100m);
        await RefundAsync("PAY-STAT-3", 30m);
        var paidDay = DateTime.UtcNow.Date.AddDays(-3);
        await MovePaymentCreationAsync(paymentId, paidDay.AddHours(12));

        var trend = await InScopeAsync<IPaymentStatisticsService, Result<List<RevenueTrendPointDto>>>(
            svc => svc.GetRevenueTrendAsync(TrendWindow()));

        trend.Data!.Count.ShouldBe(2);
        var paid = trend.Data.Single(p => p.Date == paidDay);
        paid.Revenue.ShouldBe(100m);
        paid.NetRevenue.ShouldBe(100m);

        var refundDay = trend.Data.Single(p => p.Date == DateTime.UtcNow.Date);
        refundDay.Revenue.ShouldBe(0m);
        refundDay.TransactionCount.ShouldBe(0);
        refundDay.RefundAmount.ShouldBe(30m);
        refundDay.NetRevenue.ShouldBe(-30m);

        var monthly = await InScopeAsync<IPaymentStatisticsService, Result<List<RevenueTrendPointDto>>>(
            svc => svc.GetRevenueTrendAsync(new RevenueTrendQueryDto
            {
                StartTime = DateTime.UtcNow.AddDays(-5),
                EndTime = DateTime.UtcNow.AddMinutes(5),
                Granularity = TrendGranularity.Month
            }));
        monthly.Data!.Sum(p => p.NetRevenue).ShouldBe(70m);
    }
}
