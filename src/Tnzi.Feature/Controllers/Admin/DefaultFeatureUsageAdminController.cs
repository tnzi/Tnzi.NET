namespace Tnzi.Feature.Controllers.Admin;

/// <summary>
/// Feature usage analytics admin controller.
/// Provides endpoints for querying feature usage statistics.
/// </summary>
[DefaultController]
[Route("admin/feature/usage")]
[ApiExplorerSettings(GroupName = "feature-admin")]
[ApiAuthorize(PermissionName = "feature.view")]
public class DefaultFeatureUsageAdminController : ApiAdminControllerBase
{
    protected readonly IFeatureUsageService FeatureUsageService;

    /// <summary>
    /// Initialize DefaultFeatureUsageAdminController
    /// </summary>
    public DefaultFeatureUsageAdminController(IFeatureUsageService featureUsageService)
    {
        FeatureUsageService = Check.NotNull(featureUsageService);
    }

    /// <summary>
    /// Get usage statistics for a specific feature
    /// </summary>
    /// <param name="featureName">Feature name</param>
    /// <param name="from">Start date filter (inclusive)</param>
    /// <param name="to">End date filter (inclusive)</param>
    [HttpGet("stats")]
    public virtual async Task<ApiResult<FeatureUsageStatsDto>> GetStats(
        [FromQuery] string featureName,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await FeatureUsageService.GetUsageStatsAsync(featureName, from, to);
        return result.ToApiResult();
    }

    /// <summary>
    /// Get usage trend data grouped by time period
    /// </summary>
    /// <param name="featureName">Feature name</param>
    /// <param name="period">Grouping period: "daily", "weekly", or "monthly"</param>
    /// <param name="from">Start date filter (inclusive)</param>
    /// <param name="to">End date filter (inclusive)</param>
    [HttpGet("trend")]
    public virtual async Task<ApiResult<List<FeatureUsageTrendDto>>> GetTrend(
        [FromQuery] string featureName,
        [FromQuery] string period = "daily",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await FeatureUsageService.GetUsageTrendAsync(featureName, period, from, to);
        return result.ToApiResult();
    }

    /// <summary>
    /// Get the most frequently checked features
    /// </summary>
    /// <param name="top">Number of top features to return (1-100)</param>
    /// <param name="from">Start date filter (inclusive)</param>
    /// <param name="to">End date filter (inclusive)</param>
    [HttpGet("most-used")]
    public virtual async Task<ApiResult<List<FeaturePopularityDto>>> GetMostUsed(
        [FromQuery] int top = 10,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await FeatureUsageService.GetMostUsedFeaturesAsync(top, from, to);
        return result.ToApiResult();
    }

    /// <summary>
    /// Cleanup old usage records beyond the retention period
    /// </summary>
    /// <param name="retentionDays">Number of days to retain records (default: 90)</param>
    [HttpDelete("cleanup")]
    public virtual async Task<ApiResult<int>> Cleanup([FromQuery] int retentionDays = 90)
    {
        var result = await FeatureUsageService.CleanupOldRecordsAsync(retentionDays);
        return result.ToApiResult();
    }
}
