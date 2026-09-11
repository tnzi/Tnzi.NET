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

    /// <summary>
    /// Origin of this feature row - "Database" (editable, persisted as
    /// <see cref="FeatureDefinition"/>) or "Code" (defined statically by
    /// an <c>IFeatureDefinitionProvider</c> implementation, not editable).
    /// </summary>
    public string Source { get; set; } = "Database";

    /// <summary>
    /// True for code-defined definitions that ship with the application binaries.
    /// Admin UI hides Edit/Delete for these rows. They also cannot carry feature
    /// values yet: <c>FeatureValue</c> references the definition by database id and a
    /// code-defined definition has none (its <see cref="Id"/> is <c>Guid.Empty</c>), so the
    /// value endpoints answer 400 <c>FEATURE_DEFINITION_NOT_OVERRIDABLE</c> for them.
    /// To make a code-defined feature overridable, create a database definition with the
    /// same name - the database row takes precedence in the merged snapshot.
    /// </summary>
    public bool IsReadOnly { get; set; }
}

// ==================== Feature Value Provider DTOs ====================

/// <summary>
/// A registered feature value provider (a "scope" values can be written to),
/// as reported by <c>GET admin/feature-values/providers</c>.
/// </summary>
public class FeatureValueProviderDto
{
    /// <summary>
    /// Canonical provider name - the exact string to send as <c>ProviderName</c>.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Evaluation priority; higher is consulted first at runtime.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Whether values under this provider are keyed (per tenant, per edition, ...).
    /// Keyed providers require <c>ProviderKey</c>; keyless ones reject it.
    /// </summary>
    public bool RequiresKey { get; set; }

    /// <summary>
    /// Whether the provider can evaluate anything in this deployment. Writes to an inactive
    /// provider are refused; its existing rows stay readable for cleanup.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Why the provider is inactive; null when active.
    /// </summary>
    public string? InactiveReason { get; set; }
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
    /// The value the runtime resolves for this feature in the requested scope: the scope's
    /// own value, else the value of a lower-priority keyless provider (Global), else the
    /// definition default. This mirrors <c>IFeatureChecker</c>'s provider chain rather than
    /// the requested row alone, so what the admin sees is what the runtime answers.
    /// </summary>
    public string EffectiveValue { get; set; } = string.Empty;

    /// <summary>
    /// Whether the requested scope holds its own value (as opposed to inheriting or defaulting)
    /// </summary>
    public bool IsExplicitlySet { get; set; }

    /// <summary>
    /// Where <see cref="EffectiveValue"/> comes from.
    /// </summary>
    public FeatureValueSource EffectiveSource { get; set; }

    /// <summary>
    /// Name of the provider whose value is effective; null when the default applies.
    /// </summary>
    public string? EffectiveProvider { get; set; }

    /// <summary>
    /// Whether the feature is enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// "Database" or "Code" - same meaning as <see cref="FeatureDefinitionDto.Source"/>.
    /// </summary>
    public string Source { get; set; } = "Database";

    /// <summary>
    /// False for code-defined definitions, which have no database id for a
    /// <c>FeatureValue</c> to reference; the UI must not offer to set a value on them.
    /// </summary>
    public bool CanOverride { get; set; } = true;
}
