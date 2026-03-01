namespace Tnzi.Feature.Entities;

/// <summary>
/// Feature definition entity
/// </summary>
public class FeatureDefinition : FullAuditedEntity<Guid>
{
    /// <summary>
    /// Feature name (unique identifier)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Display name for UI
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Feature description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Default value
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Value type
    /// </summary>
    public FeatureValueType ValueType { get; set; }

    /// <summary>
    /// Parent feature name for hierarchical grouping
    /// </summary>
    public string? ParentName { get; set; }

    /// <summary>
    /// Whether this feature definition is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Feature group for categorization
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Feature values associated with this definition
    /// </summary>
    public virtual ICollection<FeatureValue> Values { get; set; } = new List<FeatureValue>();
}
