namespace Tnzi.Feature.Services;

/// <summary>
/// Feature usage analytics service interface.
/// Provides recording and querying of feature usage data.
/// </summary>
public interface IFeatureUsageService
{
    /// <summary>
    /// Record a feature usage check (fire-and-forget, no Result wrapper)
    /// </summary>
    /// <param name="featureName">Feature name that was checked</param>
    /// <param name="isEnabled">Whether the feature was enabled at check time</param>
    /// <param name="source">Source of the check (e.g., "Api", "Middleware", "Manual")</param>
    Task RecordUsageAsync(string featureName, bool isEnabled, string? source = null);

    /// <summary>
    /// Get usage statistics for a specific feature
    /// </summary>
    /// <param name="featureName">Feature name</param>
    /// <param name="from">Start date filter (inclusive)</param>
    /// <param name="to">End date filter (inclusive)</param>
    Task<Result<FeatureUsageStatsDto>> GetUsageStatsAsync(string featureName, DateTime? from = null, DateTime? to = null);

    /// <summary>
    /// Get usage trend data grouped by time period
    /// </summary>
    /// <param name="featureName">Feature name</param>
    /// <param name="period">Grouping period: "daily", "weekly", or "monthly"</param>
    /// <param name="from">Start date filter (inclusive)</param>
    /// <param name="to">End date filter (inclusive)</param>
    Task<Result<List<FeatureUsageTrendDto>>> GetUsageTrendAsync(string featureName, string period = "daily", DateTime? from = null, DateTime? to = null);

    /// <summary>
    /// Get the most frequently checked features
    /// </summary>
    /// <param name="top">Number of top features to return</param>
    /// <param name="from">Start date filter (inclusive)</param>
    /// <param name="to">End date filter (inclusive)</param>
    Task<Result<List<FeaturePopularityDto>>> GetMostUsedFeaturesAsync(int top = 10, DateTime? from = null, DateTime? to = null);

    /// <summary>
    /// Cleanup old usage records beyond the retention period
    /// </summary>
    /// <param name="retentionDays">Number of days to retain records</param>
    /// <returns>Number of deleted records</returns>
    Task<Result<int>> CleanupOldRecordsAsync(int retentionDays = 90);
}
