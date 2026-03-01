namespace Tnzi.Feature.Dtos;

// ==================== Feature Definition DTOs ====================

/// <summary>
/// Feature definition DTO
/// </summary>
public class FeatureDefinitionDto
{
    /// <summary>
    /// Feature definition ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Feature name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Display name
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description
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
    /// Parent feature name
    /// </summary>
    public string? ParentName { get; set; }

    /// <summary>
    /// Whether this feature is enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Feature group
    /// </summary>
    public string? Group { get; set; }
}

/// <summary>
/// Create feature definition request
/// </summary>
public class CreateFeatureDefinitionRequest
{
    /// <summary>
    /// Feature name (unique identifier)
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Display name
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description
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
    /// Parent feature name
    /// </summary>
    public string? ParentName { get; set; }

    /// <summary>
    /// Feature group
    /// </summary>
    public string? Group { get; set; }
}

/// <summary>
/// Update feature definition request
/// </summary>
public class UpdateFeatureDefinitionRequest
{
    /// <summary>
    /// Display name
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description
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
    /// Parent feature name
    /// </summary>
    public string? ParentName { get; set; }

    /// <summary>
    /// Whether this feature is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Feature group
    /// </summary>
    public string? Group { get; set; }
}

// ==================== Feature Value DTOs ====================

/// <summary>
/// Feature value DTO
/// </summary>
public class FeatureValueDto
{
    /// <summary>
    /// Feature value ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Feature definition ID
    /// </summary>
    public Guid FeatureDefinitionId { get; set; }

    /// <summary>
    /// Feature name (from definition)
    /// </summary>
    public string? FeatureName { get; set; }

    /// <summary>
    /// Provider name
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Provider key
    /// </summary>
    public string? ProviderKey { get; set; }

    /// <summary>
    /// Feature value
    /// </summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Set feature value request
/// </summary>
public class SetFeatureValueRequest
{
    /// <summary>
    /// Feature definition ID
    /// </summary>
    public Guid FeatureDefinitionId { get; set; }

    /// <summary>
    /// Provider name (e.g., "Tenant", "Edition")
    /// </summary>
    public string ProviderName { get; set; } = null!;

    /// <summary>
    /// Provider key (e.g., tenantId)
    /// </summary>
    public string? ProviderKey { get; set; }

    /// <summary>
    /// Feature value
    /// </summary>
    public string Value { get; set; } = null!;
}

/// <summary>
/// Batch set feature values request
/// </summary>
public class BatchSetFeatureValuesRequest
{
    /// <summary>
    /// Provider name (e.g., "Tenant", "Edition")
    /// </summary>
    public string ProviderName { get; set; } = null!;

    /// <summary>
    /// Provider key (e.g., tenantId)
    /// </summary>
    public string? ProviderKey { get; set; }

    /// <summary>
    /// Feature values to set
    /// </summary>
    public List<BatchFeatureValueItem> Values { get; set; } = null!;
}

/// <summary>
/// Individual feature value in batch operation
/// </summary>
public class BatchFeatureValueItem
{
    /// <summary>
    /// Feature definition ID
    /// </summary>
    public Guid FeatureDefinitionId { get; set; }

    /// <summary>
    /// Feature value
    /// </summary>
    public string Value { get; set; } = null!;
}

/// <summary>
/// Batch set result
/// </summary>
public class BatchSetFeatureValuesResultDto
{
    /// <summary>
    /// Successfully set count
    /// </summary>
    public int SucceededCount { get; set; }

    /// <summary>
    /// Failed count
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// Error details for failed items
    /// </summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Feature value with definition info DTO (for GetAllValues)
/// </summary>
public class FeatureValueWithDefinitionDto
{
    /// <summary>
    /// Feature value ID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Feature definition ID
    /// </summary>
    public Guid FeatureDefinitionId { get; set; }

    /// <summary>
    /// Feature name
    /// </summary>
    public string FeatureName { get; set; } = string.Empty;

    /// <summary>
    /// Feature display name
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Feature description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Feature group
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Value type
    /// </summary>
    public FeatureValueType ValueType { get; set; }

    /// <summary>
    /// Default value (from definition)
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Effective value (from provider or default)
    /// </summary>
    public string EffectiveValue { get; set; } = string.Empty;

    /// <summary>
    /// Whether the value is explicitly set (vs. using default)
    /// </summary>
    public bool IsExplicitlySet { get; set; }

    /// <summary>
    /// Whether the feature is enabled
    /// </summary>
    public bool IsEnabled { get; set; }
}
