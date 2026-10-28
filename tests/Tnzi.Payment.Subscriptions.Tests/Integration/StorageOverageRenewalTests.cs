namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 计划包含存储：每天采样，续费时按上一期样本的平均超出部分收 GB-月，收据上单独一行、与计划那一行同样计税。
/// </summary>
public class StorageOverageRenewalTests : SubscriptionsIntegrationTestBase
{
    private const long Gb = StorageOverageCalculator.BytesPerGb;
    private static readonly Guid Owner = TestHelper.DefaultTestUserId;

    private readonly FakeUsageReader _reader = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<TaxOptions>(o =>
        {
            o.Enabled = true;
            o.DefaultTaxRate = 13m;
            o.Name = "HST";
        });
        services.AddSingleton<IStorageUsageReader>(_reader);
        services.AddScoped<ISubscriptionStorageMeter, SubscriptionStorageMeter>();
    }

    private async Task<Subscription> SeedDueAsync(DateTime due, long? includedBytes = 250 * Gb, decimal? pricePerGbMonth = 0.20m)
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = $"P{Guid.NewGuid():N}"[..12],
            PlanName = "Firm",
            ProductCode = "firm",
            Price = 299m,
            Currency = "CAD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true,
            IncludedStorageBytes = includedBytes,
            StorageOveragePricePerGbMonth = pricePerGbMonth
        };
        await SeedAsync(plan);

        var subscription = new Subscription
        {
            SubscriptionNo = $"SUB{Guid.NewGuid():N}"[..16],
            UserId = Owner,
            PlanId = plan.Id,
            ProductCode = "firm",
            Status = SubscriptionStatus.Active,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = due.AddMonths(-1),
            NextBillingTime = due,
            OriginalPrice = 299m,
            Currency = "CAD",
            AutoRenew = true,
            ChannelCode = "Null",
            PaymentMethodToken = "pm_card",
            LastBillingTradeNo = "PAY-SEED"
        };
        await SeedAsync(subscription);
        return subscription;
    }

    private async Task SeedSamplesAsync(Subscription subscription, DateTime firstDay, int days, long bytes)
    {
        var samples = Enumerable.Range(0, days).Select(i => new StorageUsageSample
        {
            AccountType = subscription.BillingAccountType,
            AccountId = subscription.BillingAccountId,
            SampleDate = firstDay.ToUtcDate().AddDays(i),
            StoredBytes = bytes
        });
        await SeedAsync(samples.Cast<object>().ToArray());
    }

    private Task<PaymentEntity?> RenewalPaymentAsync(Subscription subscription)
        => InScopeAsync<IRepository<PaymentEntity, Guid>, PaymentEntity?>(repo => repo.AsQueryable().AsNoTracking()
            .FirstOrDefaultAsync(p => p.BusinessOrderNo == subscription.SubscriptionNo && p.TradeNo != "PAY-SEED"));

    private static Task<IReadOnlyList<PaymentLineDescription>?> DescribeAsync(PaymentEntity payment)
        => new SubscriptionPaymentLineDescriber().DescribeAsync(new PaymentLineContext
        {
            PaymentId = payment.Id,
            BusinessType = payment.BusinessType,
            BusinessOrderNo = payment.BusinessOrderNo,
            Description = payment.Description,
            ExtraData = payment.ExtraData,
            OriginalAmount = payment.OriginalAmount,
            DiscountAmount = payment.DiscountAmount,
            TaxAmount = payment.TaxAmount,
            Currency = payment.Currency
        });

    [Fact]
    public async Task TheRenewal_ChargesLastPeriodsOverage_AsItsOwnTaxedLine()
    {
        var due = DateTime.UtcNow.AddMinutes(-5);
        var subscription = await SeedDueAsync(due);
        // 30 天的一期只采到 10 天（实例停过）：平均仍按 300 GB 算
        await SeedSamplesAsync(subscription, due.AddMonths(-1).AddDays(1), 10, 300 * Gb);

        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());

        var payment = (await RenewalPaymentAsync(subscription)).ShouldNotBeNull();
        payment.OriginalAmount.ShouldBe(309m, "CAD 299 plan + 50 GB-month at CAD 0.20");
        payment.TaxAmount.ShouldBe(40.17m, "HST 13% applies to the overage too");

        var meta = SubscriptionBillingMetadata.TryParse(payment.ExtraData).ShouldNotBeNull();
        var overage = meta.StorageOverage.ShouldNotBeNull();
        overage.Quantity.ShouldBe(50m);
        overage.UnitPrice.ShouldBe(0.20m);
        overage.Amount.ShouldBe(10m);
        overage.SampleDays.ShouldBe(10);
        overage.PeriodEnd.ShouldBe(due, TimeSpan.FromSeconds(1));

        var lines = (await DescribeAsync(payment)).ShouldNotBeNull();
        lines.Count.ShouldBe(2);
        lines[0].Description.ShouldBe("Firm");
        lines[0].Amount.ShouldBe(299m);
        var storage = lines[1];
        storage.Kind.ShouldBe(SubscriptionPaymentLineDescriber.StorageOverageLineKind);
        storage.Description.ShouldBe("Storage above 250 GB included (GB-month)");
        storage.Quantity.ShouldBe(50m);
        storage.UnitPrice.ShouldBe(0.20m);
        storage.Amount.ShouldBe(10m);
        storage.TaxAmount.ShouldBe(1.30m, "13% of 10.00");
        storage.PeriodEnd.ShouldBe(overage.PeriodEnd, "the overage is billed in arrears for the period that closed");
        (lines[0].TaxAmount + storage.TaxAmount).ShouldBe(payment.TaxAmount);
    }

    [Fact]
    public async Task APeriodWithinTheAllowance_HasNoOverageLine()
    {
        var due = DateTime.UtcNow.AddMinutes(-5);
        var subscription = await SeedDueAsync(due);
        await SeedSamplesAsync(subscription, due.AddMonths(-1).AddDays(1), 20, 200 * Gb);

        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());

        var payment = (await RenewalPaymentAsync(subscription)).ShouldNotBeNull();
        payment.OriginalAmount.ShouldBe(299m);
        SubscriptionBillingMetadata.TryParse(payment.ExtraData)!.StorageOverage.ShouldBeNull();
        (await DescribeAsync(payment)).ShouldNotBeNull().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task APeriodWithNoSampleAtAll_IsMeasuredAtRenewal_NotBilledAsZero()
    {
        var due = DateTime.UtcNow.AddMinutes(-5);
        var subscription = await SeedDueAsync(due);
        _reader.Bytes = 260 * Gb;

        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());

        var payment = (await RenewalPaymentAsync(subscription)).ShouldNotBeNull();
        SubscriptionBillingMetadata.TryParse(payment.ExtraData)!.StorageOverage.ShouldNotBeNull().Amount.ShouldBe(2.00m);
        _reader.Scopes.ShouldHaveSingleItem().Kind.ShouldBe(StorageUsageScopeKind.User);
        (await InScopeAsync<IRepository<StorageUsageSample, Guid>, int>(repo => repo.AsQueryable().CountAsync()))
            .ShouldBe(1, "the measurement is kept, so a retry of the same renewal charges the same amount");
    }

    [Fact]
    public async Task APlanWithoutAllowance_IsNotMetered()
    {
        var due = DateTime.UtcNow.AddMinutes(-5);
        var subscription = await SeedDueAsync(due, includedBytes: null);
        _reader.Bytes = 900 * Gb;

        await InScopeAsync<ISubscriptionService, Result<int>>(svc => svc.RenewExpiredSubscriptionsAsync());
        await InScopeAsync<ISubscriptionStorageMeter, Result<int>>(m => m.SampleAsync());

        (await RenewalPaymentAsync(subscription)).ShouldNotBeNull().OriginalAmount.ShouldBe(299m);
        _reader.Scopes.ShouldBeEmpty();
    }

    [Fact]
    public async Task DailySampling_KeepsOneSamplePerAccountPerDay_ForTheWholeInstallation()
    {
        var subscription = await SeedDueAsync(DateTime.UtcNow.AddDays(20));
        await InScopeAsync<IRepository<Subscription, Guid>, int>(repo => repo.AsQueryable()
            .Where(s => s.Id == subscription.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.BillingAccountType, BillingAccountTypes.Installation)
                .SetProperty(x => x.BillingAccountId, "default")));
        _reader.Bytes = 123 * Gb;

        var first = await InScopeAsync<ISubscriptionStorageMeter, Result<int>>(m => m.SampleAsync());
        var second = await InScopeAsync<ISubscriptionStorageMeter, Result<int>>(m => m.SampleAsync());

        first.Data.ShouldBe(1);
        second.Data.ShouldBe(0, "the same day again adds nothing");
        _reader.Scopes.ShouldHaveSingleItem().ShouldBe(StorageUsageScope.Installation);
        var sample = await InScopeAsync<IRepository<StorageUsageSample, Guid>, StorageUsageSample?>(repo => repo.AsQueryable().SingleOrDefaultAsync());
        sample.ShouldNotBeNull().StoredBytes.ShouldBe(123 * Gb);
        sample.SampleDate.ShouldBe(DateTime.UtcNow.ToUtcDate());
    }

    [Fact]
    public async Task TheFirmSeesUsageAgainstTheAllowance_AndTheOverageAccruedSoFar()
    {
        var periodEnd = DateTime.UtcNow.AddDays(20);
        var subscription = await SeedDueAsync(periodEnd);
        var periodStart = periodEnd.AddMonths(-1);
        await SeedSamplesAsync(subscription, periodStart.AddDays(1), 5, 300 * Gb);
        // 今天还没有样本：当前读数是当场读的那一次
        _reader.Bytes = 300 * Gb;

        var usage = (await InScopeAsync<ISubscriptionStorageMeter, Result<StorageUsageDto>>(m => m.GetUsageAsync(Owner, "firm"))).Data.ShouldNotBeNull();

        usage.Metered.ShouldBeTrue();
        usage.UsageAvailable.ShouldBeTrue();
        usage.IncludedBytes.ShouldBe(250 * Gb);
        usage.UsedBytes.ShouldBe(300 * Gb);
        usage.Subscribed.ShouldBeTrue();
        usage.SampleDays.ShouldBe(5);
        usage.OverageQuantity.ShouldBe(50m);
        usage.OverageUnitPrice.ShouldBe(0.20m);
        usage.AccruedOverageAmount.ShouldBe(10m);
        usage.Currency.ShouldBe("CAD");
        usage.PeriodEnd.ShouldNotBeNull().ShouldBe(periodEnd, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AnUnmeteredPlan_ReportsNotMetered()
    {
        await SeedDueAsync(DateTime.UtcNow.AddDays(20), includedBytes: null);

        var usage = (await InScopeAsync<ISubscriptionStorageMeter, Result<StorageUsageDto>>(m => m.GetUsageAsync(Owner, "firm"))).Data.ShouldNotBeNull();

        usage.Metered.ShouldBeFalse();
        usage.IncludedBytes.ShouldBeNull();
    }

    private sealed class FakeUsageReader : IStorageUsageReader
    {
        public long Bytes { get; set; }

        public List<StorageUsageScope> Scopes { get; } = [];

        public Task<long> GetStoredBytesAsync(StorageUsageScope scope, StorageUsageReadMode mode = StorageUsageReadMode.Measure, CancellationToken cancellationToken = default)
        {
            Scopes.Add(scope);
            return Task.FromResult(Bytes);
        }
    }
}
