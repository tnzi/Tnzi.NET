using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Caching;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 账单页在订阅之前就给出存储用量：账户此刻用了多少，对着要买的那个计划的包含量。
/// 机构决定订不订，部分就看这一行（「250 GB 里用了 180」/「已经 310 GB，会有超额」）。
/// </summary>
/// <remarks>
/// 后台任务关着的部署（开发环境）一条每日样本都没有，读数必须当场读出来，而不是永远「首次读数尚未产生」。
/// </remarks>
public class StorageUsageBeforeSubscribingTests : SubscriptionsIntegrationTestBase
{
    private const long Gb = StorageOverageCalculator.BytesPerGb;
    private static readonly Guid Owner = TestHelper.DefaultTestUserId;

    private readonly CountingReader _reader = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddMemoryCache();
        services.Configure<CachingOptions>(_ => { });
        services.AddSingleton<ICache>(sp => new MemoryCacheService(
            sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
            NullLogger<MemoryCacheService>.Instance,
            sp.GetRequiredService<IOptions<CachingOptions>>()));
        services.AddSingleton<IStorageUsageReader>(_reader);
        services.AddScoped<ISubscriptionStorageMeter, SubscriptionStorageMeter>();
    }

    private async Task<SubscriptionPlan> SeedPlanAsync(string code, long? includedBytes = 250 * Gb, decimal? price = 0.20m)
    {
        var plan = new SubscriptionPlan
        {
            PlanCode = code,
            PlanName = code,
            ProductCode = "crm",
            Price = 299m,
            Currency = "CAD",
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            IsActive = true,
            IncludedStorageBytes = includedBytes,
            StorageOveragePricePerGbMonth = price
        };
        await SeedAsync(plan);
        return plan;
    }

    private Task<Result<StorageUsageDto>> UsageAsync()
        => InScopeAsync<ISubscriptionStorageMeter, Result<StorageUsageDto>>(m => m.GetUsageAsync(Owner, "crm"));

    [Fact]
    public async Task WithoutASubscription_TheOfferedPlansAllowanceIsShown_AgainstAReadingTakenOnDemand()
    {
        await SeedPlanAsync("firm-monthly");
        _reader.Bytes = 180 * Gb;

        var usage = (await UsageAsync()).Data.ShouldNotBeNull();

        usage.Metered.ShouldBeTrue();
        usage.Subscribed.ShouldBeFalse();
        usage.UsageAvailable.ShouldBeTrue();
        usage.IncludedBytes.ShouldBe(250 * Gb);
        usage.UsedBytes.ShouldBe(180 * Gb);
        usage.SampledOn.ShouldNotBeNull();
        usage.AccruedOverageAmount.ShouldBe(0m);
        usage.OverageUnitPrice.ShouldBe(0.20m);
        _reader.Modes.ShouldBe([StorageUsageReadMode.KnownSizesOnly], "a page load must not measure thumbnails one storage round trip at a time");
    }

    [Fact]
    public async Task UsageAboveTheAllowanceBeforeSubscribing_IsShownAsSuch_WithNothingAccrued()
    {
        await SeedPlanAsync("firm-monthly");
        _reader.Bytes = 310 * Gb;

        var usage = (await UsageAsync()).Data.ShouldNotBeNull();

        usage.UsedBytes!.Value.ShouldBeGreaterThan(usage.IncludedBytes!.Value);
        usage.AccruedOverageAmount.ShouldBe(0m);
        usage.OverageQuantity.ShouldBe(0m);
    }

    [Fact]
    public async Task TheOnDemandReading_IsCached_AndNeverWrittenAsABillingSample()
    {
        await SeedPlanAsync("firm-monthly");
        _reader.Bytes = 180 * Gb;

        await UsageAsync();
        _reader.Bytes = 200 * Gb;
        var second = (await UsageAsync()).Data.ShouldNotBeNull();

        second.UsedBytes.ShouldBe(180 * Gb, "within the cache window the page does not sum the installation again");
        _reader.Modes.Count.ShouldBe(1);
        (await InScopeAsync<IRepository<StorageUsageSample, Guid>, int>(r => r.AsQueryable().CountAsync()))
            .ShouldBe(0, "billing samples come only from full measurements");
    }

    [Fact]
    public async Task TodaysDailySample_IsUsedWhenThereIsOne()
    {
        await SeedPlanAsync("firm-monthly");
        await SeedAsync(new StorageUsageSample
        {
            AccountType = BillingAccountTypes.User,
            AccountId = Owner.ToString(),
            SampleDate = DateTime.UtcNow.ToUtcDate(),
            StoredBytes = 120 * Gb
        });

        var usage = (await UsageAsync()).Data.ShouldNotBeNull();

        usage.UsedBytes.ShouldBe(120 * Gb);
        _reader.Modes.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnpaidSignUp_IsReadAgainstThePlanItChose()
    {
        await SeedPlanAsync("firm-monthly");
        var large = await SeedPlanAsync("firm-large", includedBytes: 1000 * Gb);
        await SeedAsync(new Subscription
        {
            SubscriptionNo = $"SUB{Guid.NewGuid():N}"[..16],
            UserId = Owner,
            PlanId = large.Id,
            ProductCode = "crm",
            Status = SubscriptionStatus.Pending,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            StartTime = DateTime.UtcNow,
            OriginalPrice = 299m,
            Currency = "CAD",
            ChannelCode = "Null"
        });
        _reader.Bytes = 310 * Gb;

        var usage = (await UsageAsync()).Data.ShouldNotBeNull();

        usage.Subscribed.ShouldBeFalse();
        usage.IncludedBytes.ShouldBe(1000 * Gb);
        usage.PlanName.ShouldBe("firm-large");
        usage.AccruedOverageAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task ACatalogueOfSeveralPlans_GivesUsageAlone()
    {
        await SeedPlanAsync("firm-monthly");
        await SeedPlanAsync("firm-large", includedBytes: 1000 * Gb);
        _reader.Bytes = 310 * Gb;

        var usage = (await UsageAsync()).Data.ShouldNotBeNull();

        usage.Metered.ShouldBeTrue();
        usage.UsedBytes.ShouldBe(310 * Gb);
        usage.IncludedBytes.ShouldBeNull("each plan card states its own allowance");
    }

    [Fact]
    public async Task AProductWhosePlansDoNotMeterStorage_ShowsNothing()
    {
        await SeedPlanAsync("firm-monthly", includedBytes: null, price: null);

        (await UsageAsync()).Data.ShouldNotBeNull().Metered.ShouldBeFalse();
        _reader.Modes.ShouldBeEmpty();
    }

    private sealed class CountingReader : IStorageUsageReader
    {
        public long Bytes { get; set; }

        public List<StorageUsageReadMode> Modes { get; } = [];

        public Task<long> GetStoredBytesAsync(StorageUsageScope scope, StorageUsageReadMode mode = StorageUsageReadMode.Measure, CancellationToken cancellationToken = default)
        {
            Modes.Add(mode);
            return Task.FromResult(Bytes);
        }
    }
}
