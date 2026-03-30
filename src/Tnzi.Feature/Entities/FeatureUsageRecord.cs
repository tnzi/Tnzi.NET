namespace Tnzi.Feature.Entities;

/// <summary>
/// Feature usage record entity.
/// Tracks each feature check for analytics (high volume, Snowflake ID).
/// </summary>
public class FeatureUsageRecord : CreationAuditedEntity<long>, IMultiTenant
{
    /// <summary>
    /// Feature name that was checked
    /// </summary>
    public string FeatureName { get; set; } = string.Empty;

    /// <summary>
    /// User who triggered the check (null for anonymous/system)
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Whether the feature was enabled at check time
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Source of the check (e.g., "Api", "Middleware", "Manual")
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Tenant ID for multi-tenant isolation
    /// </summary>
    public Guid? TenantId { get; set; }
}
