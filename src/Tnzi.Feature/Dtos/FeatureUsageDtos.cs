namespace Tnzi.Feature.Dtos;

/// <summary>
/// Feature usage statistics DTO
/// </summary>
public class FeatureUsageStatsDto
{
    /// <summary>
    /// Feature name
    /// </summary>
    public string FeatureName { get; set; } = string.Empty;

    /// <summary>
    /// Total number of checks
    /// </summary>
    public long TotalChecks { get; set; }

    /// <summary>
    /// Number of unique users who checked the feature
    /// </summary>
    public int UniqueUsers { get; set; }

    /// <summary>
    /// Percentage of checks where the feature was enabled (0-100)
    /// </summary>
    public decimal EnableRate { get; set; }

    /// <summary>
    /// First recorded usage time
    /// </summary>
    public DateTime? FirstUsed { get; set; }

    /// <summary>
    /// Last recorded usage time
    /// </summary>
    public DateTime? LastUsed { get; set; }
}

/// <summary>
/// Feature usage trend data point
/// </summary>
public class FeatureUsageTrendDto
{
    /// <summary>
    /// Time period label (e.g., "2026-03-26")
    /// </summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>
    /// Total check count in this period
    /// </summary>
    public long CheckCount { get; set; }

    /// <summary>
    /// Number of checks where feature was enabled
    /// </summary>
    public long EnableCount { get; set; }

    /// <summary>
    /// Number of checks where feature was disabled
    /// </summary>
    public long DisableCount { get; set; }
}

/// <summary>
/// Feature popularity ranking DTO
/// </summary>
public class FeaturePopularityDto
{
    /// <summary>
    /// Feature name
    /// </summary>
    public string FeatureName { get; set; } = string.Empty;

    /// <summary>
    /// Total check count
    /// </summary>
    public long CheckCount { get; set; }

    /// <summary>
    /// Number of unique users
    /// </summary>
    public int UniqueUsers { get; set; }

    /// <summary>
    /// Percentage of checks where the feature was enabled (0-100)
    /// </summary>
    public decimal EnableRate { get; set; }
}
