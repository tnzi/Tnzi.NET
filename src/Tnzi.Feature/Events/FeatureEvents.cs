namespace Tnzi.Feature.Events;

/// <summary>
/// Event raised when a feature definition is created
/// </summary>
public class FeatureDefinitionCreatedEvent : EventBase
{
    /// <summary>
    /// Feature definition ID
    /// </summary>
    public Guid DefinitionId { get; set; }

    /// <summary>
    /// Feature name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Feature value type
    /// </summary>
    public FeatureValueType ValueType { get; set; }

    /// <summary>
    /// Feature group
    /// </summary>
    public string? Group { get; set; }
}

/// <summary>
/// Event raised when a feature definition is updated
/// </summary>
public class FeatureDefinitionUpdatedEvent : EventBase
{
    /// <summary>
    /// Feature definition ID
    /// </summary>
    public Guid DefinitionId { get; set; }

    /// <summary>
    /// Feature name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether the feature is enabled
    /// </summary>
    public bool IsEnabled { get; set; }
}

/// <summary>
/// Event raised when a feature definition is deleted
/// </summary>
public class FeatureDefinitionDeletedEvent : EventBase
{
    /// <summary>
    /// Feature definition ID
    /// </summary>
    public Guid DefinitionId { get; set; }

    /// <summary>
    /// Feature name
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Event raised when a feature value is set or updated
/// </summary>
public class FeatureValueChangedEvent : EventBase
{
    /// <summary>
    /// Feature name
    /// </summary>
    public string FeatureName { get; set; } = string.Empty;

    /// <summary>
    /// Provider name (e.g., "Tenant", "Edition")
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Provider key (e.g., tenantId)
    /// </summary>
    public string? ProviderKey { get; set; }

    /// <summary>
    /// New value
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Previous value (null if new)
    /// </summary>
    public string? PreviousValue { get; set; }
}

/// <summary>
/// Event raised when a feature value is deleted
/// </summary>
public class FeatureValueDeletedEvent : EventBase
{
    /// <summary>
    /// Feature name
    /// </summary>
    public string FeatureName { get; set; } = string.Empty;

    /// <summary>
    /// Provider name
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Provider key
    /// </summary>
    public string? ProviderKey { get; set; }
}
