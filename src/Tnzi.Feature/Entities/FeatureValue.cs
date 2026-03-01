namespace Tnzi.Feature.Entities;

/// <summary>
/// Feature value entity - stores provider-specific value overrides
/// </summary>
public class FeatureValue : FullAuditedEntity<Guid>
{
    /// <summary>
    /// Feature definition ID (FK)
    /// </summary>
    public Guid FeatureDefinitionId { get; set; }

    /// <summary>
    /// Provider name (e.g., "Tenant", "Edition")
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Provider key (e.g., tenantId, editionId)
    /// </summary>
    public string? ProviderKey { get; set; }

    /// <summary>
    /// Feature value
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Navigation property to feature definition
    /// </summary>
    public virtual FeatureDefinition? FeatureDefinition { get; set; }
}
