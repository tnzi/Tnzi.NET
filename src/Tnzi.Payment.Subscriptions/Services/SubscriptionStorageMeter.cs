namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// <see cref="ISubscriptionStorageMeter"/> 的实现。算法见 <see cref="StorageOverageCalculator"/>。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>一期没有一个样本时</b>（整期实例都没跑采样，或订阅刚开通就到了计费日）：先取计费日当天或之后最早的那条样本，
/// 再没有就当场采一条（记作今天的样本）。所以账单不会因为没采到而变成 0，同一期的重试（PastDue 后再扣）
/// 也拿到同一个数：过去的日子不会再长出新样本。
/// </para>
/// <para>
/// 计量的范围是计费账户：用户账户 = 这个用户名下的文件，租户账户 = 这个租户的全部文件，
/// 安装账户 = 整个安装的全部文件（越过租户）。其它（消费应用自定义的）账户类型不计量。
/// </para>
/// </remarks>
public class SubscriptionStorageMeter : ApplicationService, ISubscriptionStorageMeter
{
    /// <summary>每轮最多给多少个账户采样。</summary>
    public const int MaxAccountsPerRun = 500;

    private static readonly SubscriptionStatus[] MeteredStatuses =
    [
        SubscriptionStatus.Trial,
        SubscriptionStatus.Active,
        SubscriptionStatus.PendingRenewal,
        SubscriptionStatus.PastDue,
        SubscriptionStatus.Suspended,
        SubscriptionStatus.Paused
    ];

    /// <summary>请求路径上当场读出的用量缓存多久（整个安装的总和是一条聚合查询，账单页每次打开都算一遍不值得）。</summary>
    public static readonly TimeSpan OnDemandReadingTtl = TimeSpan.FromMinutes(10);

    private static int _readerMissingReported;

    private readonly IRepository<Subscription, Guid> _subscriptions;
    private readonly IRepository<StorageUsageSample, Guid> _samples;
    private readonly IRepository<SubscriptionPlan, Guid> _plans;
    private readonly IBillingAccountResolver _accountResolver;
    private readonly IStorageUsageReader? _usageReader;
    private readonly ICache? _cache;

    public SubscriptionStorageMeter(
        IRepository<Subscription, Guid> subscriptions,
        IRepository<StorageUsageSample, Guid> samples,
        IRepository<SubscriptionPlan, Guid> plans,
        IBillingAccountResolver accountResolver,
        IServiceProvider serviceProvider,
        IStorageUsageReader? usageReader = null,
        ICache? cache = null)
        : base(serviceProvider)
    {
        _subscriptions = Check.NotNull(subscriptions);
        _samples = Check.NotNull(samples);
        _plans = Check.NotNull(plans);
        _accountResolver = Check.NotNull(accountResolver);
        _usageReader = usageReader;
        _cache = cache;
    }

    public async Task<Result<int>> SampleAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await _subscriptions.AsNoTracking()
            .Where(s => MeteredStatuses.Contains(s.Status) && s.Plan != null && s.Plan.IncludedStorageBytes != null)
            .Select(s => new { s.BillingAccountType, s.BillingAccountId })
            .Distinct()
            .OrderBy(a => a.BillingAccountType).ThenBy(a => a.BillingAccountId)
            .Take(MaxAccountsPerRun)
            .ToListAsync(cancellationToken);

        if (accounts.Count == 0)
            return Ok(0);

        if (_usageReader == null)
        {
            ReportReaderMissing();
            return Ok(0);
        }

        var today = DateTime.UtcNow.ToUtcDate();
        var sampledToday = (await _samples.AsNoTracking()
                .Where(s => s.SampleDate == today)
                .Select(s => new { s.AccountType, s.AccountId })
                .ToListAsync(cancellationToken))
            .Select(s => (s.AccountType, s.AccountId))
            .ToHashSet();

        var recorded = 0;
        foreach (var account in accounts)
        {
            if (sampledToday.Contains((account.BillingAccountType, account.BillingAccountId)))
                continue;

            try
            {
                if (await RecordSampleAsync(account.BillingAccountType, account.BillingAccountId, today, cancellationToken) != null)
                    recorded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "Storage usage sample for billing account {AccountType}:{AccountId} failed; it is retried on the next round.",
                    account.BillingAccountType, account.BillingAccountId);
            }
        }

        return Ok(recorded);
    }

    public async Task<StorageOverageCharge?> PriceClosingPeriodAsync(Subscription subscription, SubscriptionPlan plan, bool measureWhenUnsampled = true, CancellationToken cancellationToken = default)
    {
        Check.NotNull(subscription);
        Check.NotNull(plan);

        if (plan.IncludedStorageBytes is not { } included)
            return null;

        var end = subscription.NextBillingTime ?? DateTime.UtcNow;
        var start = StorageOverageCalculator.PeriodStart(subscription.CycleType, subscription.CycleValue, end, EarliestBillableStart(subscription));
        if (start >= end)
            return null;

        var samples = await SamplesInAsync(subscription, start, end, cancellationToken);
        if (samples.Count == 0)
            samples = await FallbackSampleAsync(subscription, end, measureWhenUnsampled, cancellationToken);

        if (samples.Count == 0)
        {
            if (!measureWhenUnsampled)
                return null;

            Logger.LogWarning(
                "No storage usage could be measured for subscription {SubscriptionNo} (period {Start:o} to {End:o}); the renewal is charged without a storage overage.",
                subscription.SubscriptionNo, start, end);
            return null;
        }

        var quote = StorageOverageCalculator.Quote(
            samples, included, plan.StorageOveragePricePerGbMonth ?? 0m,
            StorageOverageCalculator.MonthsIn(subscription.CycleType, subscription.CycleValue, start, end),
            subscription.Currency);

        if (quote is not { Amount: > 0m })
            return null;

        return new StorageOverageCharge
        {
            PeriodStart = start,
            PeriodEnd = end,
            SampleDays = quote.SampleDays,
            AverageBytes = quote.AverageBytes,
            IncludedBytes = quote.IncludedBytes,
            Quantity = quote.Quantity,
            UnitPrice = quote.UnitPrice,
            Amount = quote.Amount
        };
    }

    public async Task<Result<StorageUsageDto>> GetUsageAsync(Guid userId, string? productCode = null, CancellationToken cancellationToken = default)
    {
        var access = await _accountResolver.ResolveAsync(userId, cancellationToken);
        if (access == null || !access.CanView)
            return Fail<StorageUsageDto>(ErrorCodes.BillingAccountAccessDenied, 403);

        var account = access.Account;
        var product = string.IsNullOrWhiteSpace(productCode) ? null : productCode.Trim();
        var subscription = await _subscriptions.AsNoTracking()
            .Where(s => s.BillingAccountType == account.Type && s.BillingAccountId == account.Id
                && (product == null || s.ProductCode == product)
                && MeteredStatuses.Contains(s.Status))
            .Include(s => s.Plan)
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription == null)
            return Ok(await OfferedUsageAsync(account, product, cancellationToken));

        var plan = subscription.Plan;
        if (plan?.IncludedStorageBytes is not { } included)
            return Ok(new StorageUsageDto());

        var reading = await CurrentReadingAsync(account, cancellationToken);
        var end = subscription.NextBillingTime;
        var start = end is { } periodEnd
            ? StorageOverageCalculator.PeriodStart(subscription.CycleType, subscription.CycleValue, periodEnd, EarliestPeriodStart(subscription))
            : (DateTime?)null;

        var dto = new StorageUsageDto
        {
            Metered = true,
            Subscribed = true,
            UsageAvailable = _usageReader != null,
            SubscriptionId = subscription.Id,
            PlanName = plan.PlanName,
            IncludedBytes = included,
            UsedBytes = reading?.Bytes,
            SampledOn = reading?.MeasuredAt,
            PeriodStart = start,
            PeriodEnd = end,
            OverageUnitPrice = plan.StorageOveragePricePerGbMonth is > 0m ? plan.StorageOveragePricePerGbMonth : null,
            Currency = subscription.Currency,
            InTrial = subscription.Status == SubscriptionStatus.Trial
        };

        if (start is not { } from || end is not { } to)
            return Ok(dto);

        var samples = await SamplesInAsync(subscription, from, to, cancellationToken);
        var quote = StorageOverageCalculator.Quote(
            samples, included, plan.StorageOveragePricePerGbMonth ?? 0m,
            StorageOverageCalculator.MonthsIn(subscription.CycleType, subscription.CycleValue, from, to),
            subscription.Currency);
        if (quote == null)
            return Ok(dto);

        dto.SampleDays = quote.SampleDays;
        dto.AverageBytes = quote.AverageBytes;
        dto.OverageQuantity = quote.Quantity;
        dto.AccruedOverageAmount = dto.InTrial ? 0m : quote.Amount;
        return Ok(dto);
    }

    /// <summary>
    /// 还没有在用的订阅（没订、或开通了但首期款没付）：账户此刻的用量，对着要买的那个计划的包含量。
    /// </summary>
    /// <remarks>
    /// 要买的计划：未付的开通单所选的那个；否则这个产品唯一的启用计划。产品卖几个计划时只给用量、不给包含量
    /// （每张计划卡片各自写着自己的包含量）。产品没有任何计量存储的计划时不显示。没有订阅就不累计超额。
    /// </remarks>
    private async Task<StorageUsageDto> OfferedUsageAsync(BillingAccountRef account, string? product, CancellationToken cancellationToken)
    {
        var pending = await _subscriptions.AsNoTracking()
            .Where(s => s.BillingAccountType == account.Type && s.BillingAccountId == account.Id
                && (product == null || s.ProductCode == product)
                && s.Status == SubscriptionStatus.Pending)
            .Include(s => s.Plan)
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefaultAsync(cancellationToken);

        List<SubscriptionPlan> offered = pending?.Plan is { } chosen
            ? [chosen]
            : await _plans.AsNoTracking()
                .Where(p => p.IsActive && (product == null || p.ProductCode == product))
                .ToListAsync(cancellationToken);

        if (!offered.Any(p => p.IncludedStorageBytes != null))
            return new StorageUsageDto();

        var single = offered.Count == 1 ? offered[0] : null;
        var reading = await CurrentReadingAsync(account, cancellationToken);
        return new StorageUsageDto
        {
            Metered = true,
            Subscribed = false,
            UsageAvailable = _usageReader != null,
            SubscriptionId = pending?.Id,
            PlanName = single?.PlanName,
            IncludedBytes = single?.IncludedStorageBytes,
            UsedBytes = reading?.Bytes,
            SampledOn = reading?.MeasuredAt,
            OverageUnitPrice = single?.StorageOveragePricePerGbMonth is > 0m ? single.StorageOveragePricePerGbMonth : null,
            Currency = single?.Currency
        };
    }

    /// <summary>
    /// 账户此刻的用量读数：今天（UTC）已有每日样本就用它；否则当场读一次已知大小的总和，缓存 <see cref="OnDemandReadingTtl"/>。
    /// </summary>
    /// <remarks>
    /// ★ 当场读数<b>不写成样本</b>：样本是计费的依据，要由带缩略图测量的完整读数产生（后台采样、续费时的补量），
    /// 而请求路径上只做一条聚合查询（<see cref="StorageUsageReadMode.KnownSizesOnly"/>）。
    /// 这让后台任务关着的部署（开发环境）也有读数，而不是永远「首次读数尚未产生」。读不到时退回最近一条样本。
    /// </remarks>
    private async Task<StorageReading?> CurrentReadingAsync(BillingAccountRef account, CancellationToken cancellationToken)
    {
        var latest = await _samples.AsNoTracking()
            .Where(s => s.AccountType == account.Type && s.AccountId == account.Id)
            .OrderByDescending(s => s.SampleDate)
            .Select(s => new { s.SampleDate, s.StoredBytes })
            .FirstOrDefaultAsync(cancellationToken);
        var fromSample = latest == null ? null : new StorageReading(latest.StoredBytes, latest.SampleDate);

        if (latest != null && latest.SampleDate == DateTime.UtcNow.ToUtcDate())
            return fromSample;

        if (_usageReader == null || ScopeOf(account.Type, account.Id) is not { } scope)
            return fromSample;

        var key = $"tnzi:payment:storage-reading:{account.Type}:{account.Id}";
        if (_cache != null && await _cache.GetAsync<StorageReading>(key, cancellationToken) is { } cached)
            return cached;

        try
        {
            var reading = new StorageReading(
                await _usageReader.GetStoredBytesAsync(scope, StorageUsageReadMode.KnownSizesOnly, cancellationToken),
                DateTime.UtcNow);
            if (_cache != null)
                await _cache.SetAsync(key, reading, OnDemandReadingTtl, cancellationToken);
            return reading;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Storage usage for billing account {AccountType}:{AccountId} could not be read on demand; showing the latest daily sample.",
                account.Type, account.Id);
            return fromSample;
        }
    }

    /// <summary>一次用量读数：字节数与测量时刻（每日样本是那一天的 UTC 零点）。</summary>
    public sealed record StorageReading(long Bytes, DateTime MeasuredAt);

    /// <summary>
    /// 计费账户对应的存储范围；不认识的账户类型返回 null（不计量）。
    /// </summary>
    public static StorageUsageScope? ScopeOf(string accountType, string accountId) => accountType switch
    {
        BillingAccountTypes.Installation => StorageUsageScope.Installation,
        BillingAccountTypes.Tenant when Guid.TryParse(accountId, out var tenantId) => StorageUsageScope.ForTenant(tenantId),
        BillingAccountTypes.User when Guid.TryParse(accountId, out var userId) => StorageUsageScope.ForUser(userId),
        _ => null
    };

    /// <summary>可计超额的最早时刻：订阅开始，试用过的从试用结束起（试用期不收超额）。</summary>
    private static DateTime EarliestBillableStart(Subscription subscription)
        => subscription.TrialEndTime is { } trialEnd && trialEnd > subscription.StartTime ? trialEnd : subscription.StartTime;

    /// <summary>当前这一期最早从哪里算：试用中就是订阅开始，否则同 <see cref="EarliestBillableStart"/>。</summary>
    private static DateTime EarliestPeriodStart(Subscription subscription)
        => subscription.Status == SubscriptionStatus.Trial ? subscription.StartTime : EarliestBillableStart(subscription);

    private async Task<List<long>> SamplesInAsync(Subscription subscription, DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        var from = start.ToUtcDate();
        var to = end.ToUtcDate();
        return await _samples.AsNoTracking()
            .Where(s => s.AccountType == subscription.BillingAccountType && s.AccountId == subscription.BillingAccountId
                && s.SampleDate >= from && s.SampleDate < to)
            .Select(s => s.StoredBytes)
            .ToListAsync(cancellationToken);
    }

    /// <summary>一期没有样本时：计费日当天或之后最早的那条；再没有就当场采一条。</summary>
    private async Task<List<long>> FallbackSampleAsync(Subscription subscription, DateTime end, bool measure, CancellationToken cancellationToken)
    {
        var day = end.ToUtcDate();
        var next = await _samples.AsNoTracking()
            .Where(s => s.AccountType == subscription.BillingAccountType && s.AccountId == subscription.BillingAccountId && s.SampleDate >= day)
            .OrderBy(s => s.SampleDate)
            .Select(s => (long?)s.StoredBytes)
            .FirstOrDefaultAsync(cancellationToken);
        if (next is { } bytes)
            return [bytes];

        if (!measure)
            return [];

        if (_usageReader == null)
        {
            ReportReaderMissing();
            return [];
        }

        var measured = await RecordSampleAsync(subscription.BillingAccountType, subscription.BillingAccountId, DateTime.UtcNow.ToUtcDate(), cancellationToken);
        return measured is { } value ? [value] : [];
    }

    /// <summary>量一次并记作 <paramref name="day"/> 的样本；同一天已有一条时返回已有的那条。账户类型不计量时返回 null。</summary>
    private async Task<long?> RecordSampleAsync(string accountType, string accountId, DateTime day, CancellationToken cancellationToken)
    {
        if (ScopeOf(accountType, accountId) is not { } scope)
        {
            Logger.LogWarning("Billing account type {AccountType} has no storage scope; its storage is not metered.", accountType);
            return null;
        }

        var bytes = await _usageReader!.GetStoredBytesAsync(scope, StorageUsageReadMode.Measure, cancellationToken);
        var sample = new StorageUsageSample { AccountType = accountType, AccountId = accountId, SampleDate = day, StoredBytes = bytes };
        try
        {
            await _samples.InsertAsync(sample, cancellationToken);
            return bytes;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 另一个实例刚记下今天这一条：以它为准
            _samples.Discard(sample);
            return await _samples.AsNoTracking()
                .Where(s => s.AccountType == accountType && s.AccountId == accountId && s.SampleDate == day)
                .Select(s => (long?)s.StoredBytes)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }

    private void ReportReaderMissing()
    {
        if (Interlocked.Exchange(ref _readerMissingReported, 1) == 0)
        {
            Logger.LogWarning(
                "A subscription plan includes storage, but no IStorageUsageReader is registered, so storage is not sampled and no overage is charged. Load the Tnzi.Storage module.");
        }
    }
}
