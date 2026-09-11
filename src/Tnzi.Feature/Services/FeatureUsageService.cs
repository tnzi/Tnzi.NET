namespace Tnzi.Feature.Services;

/// <summary>
/// Feature usage analytics service implementation.
/// Records feature checks and provides usage statistics.
/// </summary>
public class FeatureUsageService : ApplicationService, IFeatureUsageService
{
    private readonly IRepository<FeatureUsageRecord, long> _repository;
    private readonly IFeatureUsageSender _sender;
    private readonly IOptionsMonitor<FeatureOptions> _options;
    private readonly ICurrentTenant? _currentTenant;

    /// <summary>
    /// Initialize FeatureUsageService
    /// </summary>
    /// <param name="serviceProvider">Service provider.</param>
    /// <param name="repository">Usage record repository (analytics queries and cleanup).</param>
    /// <param name="sender">Request-side queue the records are handed to; a hosted service writes them in batches.</param>
    /// <param name="options">Feature options, read hot for <see cref="FeatureOptions.UsageTrackingEnabled"/>.</param>
    /// <param name="currentTenant">Current tenant accessor, absent when multi-tenancy is not loaded.</param>
    public FeatureUsageService(
        IServiceProvider serviceProvider,
        IRepository<FeatureUsageRecord, long> repository,
        IFeatureUsageSender sender,
        IOptionsMonitor<FeatureOptions> options,
        ICurrentTenant? currentTenant = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _sender = Check.NotNull(sender);
        _options = Check.NotNull(options);
        _currentTenant = currentTenant;
    }

    /// <inheritdoc />
    public Task RecordUsageAsync(string featureName, bool isEnabled, string? source = null)
    {
        Check.NotNullOrWhiteSpace(featureName);

        if (!_options.CurrentValue.UsageTrackingEnabled)
        {
            return Task.CompletedTask;
        }

        // 谁、哪个租户、什么时候 —— 全部在入队这一刻定格。后台落库的作用域里没有当前用户也没有
        // 当前租户，而 SaveChanges 的审计填充只补空值，不会替我们找回请求上下文。
        // 租户取法与 EF 审计填充同一优先级：ICurrentTenant 先、用户声明里的租户其次。
        var record = new FeatureUsageRecord
        {
            FeatureName = featureName,
            UserId = CurrentUser?.Id,
            TenantId = _currentTenant?.Id ?? CurrentUser?.TenantId,
            IsEnabled = isEnabled,
            Source = source,
            CreationTime = DateTime.UtcNow
        };

        // 非阻塞：队列满时丢弃并由 sender 计数记日志，绝不让功能检查等一次写库。
        _sender.TrySend(record);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<Result<FeatureUsageStatsDto>> GetUsageStatsAsync(string featureName, DateTime? from = null, DateTime? to = null)
    {
        Check.NotNullOrWhiteSpace(featureName);

        var query = BuildFilteredQuery(featureName, from, to);

        var totalChecks = await query.LongCountAsync();
        if (totalChecks == 0)
        {
            return Ok(new FeatureUsageStatsDto
            {
                FeatureName = featureName,
                TotalChecks = 0,
                UniqueUsers = 0,
                EnableRate = 0m
            });
        }

        var uniqueUsers = await query.Where(r => r.UserId != null)
            .Select(r => r.UserId)
            .Distinct()
            .CountAsync();

        var enabledCount = await query.LongCountAsync(r => r.IsEnabled);

        var firstUsed = await query.MinAsync(r => (DateTime?)r.CreationTime);
        var lastUsed = await query.MaxAsync(r => (DateTime?)r.CreationTime);

        var enableRate = totalChecks > 0
            ? Math.Round((decimal)enabledCount / totalChecks * 100, 2)
            : 0m;

        return Ok(new FeatureUsageStatsDto
        {
            FeatureName = featureName,
            TotalChecks = totalChecks,
            UniqueUsers = uniqueUsers,
            EnableRate = enableRate,
            FirstUsed = firstUsed,
            LastUsed = lastUsed
        });
    }

    /// <inheritdoc />
    public async Task<Result<List<FeatureUsageTrendDto>>> GetUsageTrendAsync(
        string featureName, string period = "daily", DateTime? from = null, DateTime? to = null)
    {
        Check.NotNullOrWhiteSpace(featureName);

        if (!TryParsePeriod(period, out var interval))
        {
            return Fail<List<FeatureUsageTrendDto>>("Period must be 'daily', 'weekly', or 'monthly'", 400);
        }

        var query = BuildFilteredQuery(featureName, from, to);

        // 数据库端只按天聚合（TimeBucket 是内存原语，翻译不成 SQL），周/月在这份按天的小结果集上
        // 二次汇总——三个计数都是可加的，故与直接按周/月聚合等价。
        // 同理 Period 的字符串格式化必须留在内存：DateTime.ToString(format) 写进投影会在运行时抛
        // "could not be translated"。
        var daily = await query
            .GroupBy(r => r.CreationTime.Date)
            .Select(g => new
            {
                Date = g.Key,
                CheckCount = g.LongCount(),
                EnableCount = g.LongCount(r => r.IsEnabled),
                DisableCount = g.LongCount(r => !r.IsEnabled)
            })
            .ToListAsync();

        var trend = daily
            .GroupBy(d => TimeBucket.Label(d.Date, interval))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new FeatureUsageTrendDto
            {
                Period = g.Key,
                CheckCount = g.Sum(d => d.CheckCount),
                EnableCount = g.Sum(d => d.EnableCount),
                DisableCount = g.Sum(d => d.DisableCount)
            })
            .ToList();

        return Ok(trend);
    }

    /// <summary>
    /// 把对外的 period 字符串（线缆契约，保持小写）解析成核心 <see cref="TrendInterval"/>。
    /// </summary>
    private static bool TryParsePeriod(string? period, out TrendInterval interval)
    {
        switch (period)
        {
            case "daily": interval = TrendInterval.Daily; return true;
            case "weekly": interval = TrendInterval.Weekly; return true;
            case "monthly": interval = TrendInterval.Monthly; return true;
            default: interval = TrendInterval.Daily; return false;
        }
    }

    /// <inheritdoc />
    public async Task<Result<List<FeaturePopularityDto>>> GetMostUsedFeaturesAsync(
        int top = 10, DateTime? from = null, DateTime? to = null)
    {
        if (top is < 1 or > 100)
        {
            return Fail<List<FeaturePopularityDto>>("Top must be between 1 and 100", 400);
        }

        var query = _repository.AsQueryable().AsNoTracking();
        query = ApplyDateFilter(query, from, to);

        var result = await query
            .GroupBy(r => r.FeatureName)
            .Select(g => new FeaturePopularityDto
            {
                FeatureName = g.Key,
                CheckCount = g.LongCount(),
                UniqueUsers = g.Where(r => r.UserId != null).Select(r => r.UserId).Distinct().Count(),
                EnableRate = g.LongCount() > 0
                    ? Math.Round((decimal)g.LongCount(r => r.IsEnabled) / g.LongCount() * 100, 2)
                    : 0m
            })
            .OrderByDescending(p => p.CheckCount)
            .Take(top)
            .ToListAsync();

        return Ok(result);
    }

    /// <inheritdoc />
    public async Task<Result<int>> CleanupOldRecordsAsync(int retentionDays = 90)
    {
        if (retentionDays < 1)
        {
            return Fail<int>("Retention days must be at least 1", 400);
        }

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        // ExecuteDelete 绕过变更跟踪直发 SQL：环境事务下物理事务默认延迟到首次
        // SaveChanges 才开启，不先确保开启则这条删除在自动提交模式下执行，脱离事务保护。
        await _repository.EnsureTransactionStartedAsync();

        var deleted = await _repository
            .Where(r => r.CreationTime < cutoff)
            .ExecuteDeleteAsync();

        Logger.LogInformation("Cleaned up {Count} feature usage records older than {Days} days", deleted, retentionDays);

        return Ok(deleted);
    }

    /// <summary>
    /// Build a filtered queryable for a specific feature with optional date range
    /// </summary>
    private IQueryable<FeatureUsageRecord> BuildFilteredQuery(string featureName, DateTime? from, DateTime? to)
    {
        var query = _repository.AsQueryable().AsNoTracking()
            .Where(r => r.FeatureName == featureName);

        return ApplyDateFilter(query, from, to);
    }

    /// <summary>
    /// Apply date range filters to a query
    /// </summary>
    private static IQueryable<FeatureUsageRecord> ApplyDateFilter(IQueryable<FeatureUsageRecord> query, DateTime? from, DateTime? to)
    {
        if (from.HasValue)
        {
            query = query.Where(r => r.CreationTime >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(r => r.CreationTime <= to.Value);
        }

        return query;
    }
}
