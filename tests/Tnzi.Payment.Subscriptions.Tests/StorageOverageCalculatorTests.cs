namespace Tnzi.Payment.Subscriptions.Tests;

/// <summary>
/// 超额存储费的算法：一期样本的平均减去包含量，按 GB-月（1 GB = 2^30 字节）计价。
/// </summary>
public class StorageOverageCalculatorTests
{
    private const long Gb = StorageOverageCalculator.BytesPerGb;

    [Fact]
    public void AGb_Is2ToThe30Bytes()
        => StorageOverageCalculator.BytesPerGb.ShouldBe(1_073_741_824L);

    [Fact]
    public void MissingDays_NeitherZeroNorInflateTheBill()
    {
        // 30 天的一期只采到 10 天，每天都是 300 GB：平均仍是 300 GB，不是 100 GB（缺的天按 0）也不是更多
        var tenDays = Enumerable.Repeat(300 * Gb, 10).ToArray();
        var thirtyDays = Enumerable.Repeat(300 * Gb, 30).ToArray();

        var partial = StorageOverageCalculator.Quote(tenDays, 250 * Gb, 0.20m, 1m, "CAD").ShouldNotBeNull();
        var full = StorageOverageCalculator.Quote(thirtyDays, 250 * Gb, 0.20m, 1m, "CAD").ShouldNotBeNull();

        partial.SampleDays.ShouldBe(10);
        partial.AverageBytes.ShouldBe(300 * Gb);
        partial.Quantity.ShouldBe(50m);
        partial.Amount.ShouldBe(10.00m);
        partial.Amount.ShouldBe(full.Amount);
    }

    [Fact]
    public void TheAverage_IsOverTheSampledDays()
    {
        var quote = StorageOverageCalculator.Quote([200 * Gb, 300 * Gb, 400 * Gb], 250 * Gb, 0.20m, 1m, "CAD").ShouldNotBeNull();

        quote.AverageBytes.ShouldBe(300 * Gb);
        quote.Quantity.ShouldBe(50m);
        quote.Amount.ShouldBe(10.00m);
    }

    [Fact]
    public void UsageAtOrUnderTheAllowance_IsNoOverage()
    {
        var quote = StorageOverageCalculator.Quote([100 * Gb, 250 * Gb], 250 * Gb, 0.20m, 1m, "CAD").ShouldNotBeNull();

        quote.Quantity.ShouldBe(0m);
        quote.Amount.ShouldBe(0m);
    }

    [Fact]
    public void NoSamples_IsNotZero_ItIsUnknown()
        => StorageOverageCalculator.Quote([], 250 * Gb, 0.20m, 1m, "CAD").ShouldBeNull();

    [Fact]
    public void TheAmount_IsTheRoundedQuantityTimesThePrice()
    {
        // 平均超出 1.2346 GB：数量取三位 1.235，金额 = 1.235 × 0.20 = 0.247 → 0.25
        var average = 250 * Gb + (long)(1.2346m * Gb);
        var quote = StorageOverageCalculator.Quote([average], 250 * Gb, 0.20m, 1m, "CAD").ShouldNotBeNull();

        quote.Quantity.ShouldBe(1.235m);
        quote.Amount.ShouldBe(0.25m);
    }

    [Fact]
    public void AZeroPrice_MetersWithoutCharging()
    {
        var quote = StorageOverageCalculator.Quote([300 * Gb], 250 * Gb, 0m, 1m, "CAD").ShouldNotBeNull();

        quote.Quantity.ShouldBe(50m);
        quote.Amount.ShouldBe(0m);
    }

    [Fact]
    public void GbMonths_FollowTheBillingCycle()
    {
        var end = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

        StorageOverageCalculator.MonthsIn(BillingCycleType.Month, 1, end.AddMonths(-1), end).ShouldBe(1m);
        StorageOverageCalculator.MonthsIn(BillingCycleType.Year, 1, end.AddYears(-1), end).ShouldBe(12m);
        StorageOverageCalculator.MonthsIn(BillingCycleType.Week, 1, end.AddDays(-7), end).ShouldBe(7m / 30m);
        // 期中开始的一期按天数比例折算
        StorageOverageCalculator.MonthsIn(BillingCycleType.Month, 1, end.AddDays(-15), end).ShouldBe(15m / 31m, 0.0001m);
    }

    [Fact]
    public void TheClosingPeriod_IsOneCycleBack_ButNotBeforeTheSubscriptionStarted()
    {
        var end = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

        StorageOverageCalculator.PeriodStart(BillingCycleType.Month, 1, end, end.AddYears(-1)).ShouldBe(end.AddMonths(-1));
        StorageOverageCalculator.PeriodStart(BillingCycleType.Month, 1, end, end.AddDays(-10)).ShouldBe(end.AddDays(-10));
    }
}
