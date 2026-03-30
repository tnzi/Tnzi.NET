namespace Tnzi.Feature.Services;

/// <summary>
/// Feature usage analytics service implementation.
/// Records feature checks and provides usage statistics.
/// </summary>
public class FeatureUsageService : ApplicationService, IFeatureUsageService
{
    private readonly IRepository<FeatureUsageRecord, long> _repository;

    /// <summary>
    /// Initialize FeatureUsageService
    /// </summary>
    public FeatureUsageService(
        IServiceProvider serviceProvider,
        IRepository<FeatureUsageRecord, long> repository)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
    }

    /// <inheritdoc />
    public async Task RecordUsageAsync(string featureName, bool isEnabled, string? source = null)
    {
        Check.NotNullOrWhiteSpace(featureName);

        try
        {
            var record = new FeatureUsageRecord
            {
                FeatureName = featureName,
                UserId = CurrentUser?.Id,
                IsEnabled = isEnabled,
                Source = source
            };

            await _repository.InsertAsync(record);
        }
        catch (Exception ex)
        {
            // Fire-and-forget: log but do not propagate errors
            Logger.LogWarning(ex, "Failed to record feature usage for '{FeatureName}'", featureName);
        }
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

        if (period is not ("daily" or "weekly" or "monthly"))
        {
            return Fail<List<FeatureUsageTrendDto>>("Period must be 'daily', 'weekly', or 'monthly'", 400);
        }

        var query = BuildFilteredQuery(featureName, from, to);

        // Group by period using database-side date truncation
        var grouped = period switch
        {
            "daily" => query.GroupBy(r => r.CreationTime.Date),
            "weekly" => query.GroupBy(r => r.CreationTime.Date.AddDays(-(int)r.CreationTime.DayOfWeek)),
            "monthly" => query.GroupBy(r => new DateTime(r.CreationTime.Year, r.CreationTime.Month, 1)),
            _ => query.GroupBy(r => r.CreationTime.Date)
        };

        var trend = await grouped
            .Select(g => new FeatureUsageTrendDto
            {
                Period = g.Key.ToString("yyyy-MM-dd"),
                CheckCount = g.LongCount(),
                EnableCount = g.LongCount(r => r.IsEnabled),
                DisableCount = g.LongCount(r => !r.IsEnabled)
            })
            .OrderBy(t => t.Period)
            .ToListAsync();

        return Ok(trend);
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
