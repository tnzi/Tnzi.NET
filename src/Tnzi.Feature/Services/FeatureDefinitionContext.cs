namespace Tnzi.Feature.Services;

/// <summary>
/// Default implementation of IFeatureDefinitionContext
/// </summary>
public class FeatureDefinitionContext : IFeatureDefinitionContext
{
    private readonly Dictionary<string, FeatureDefinitionRecord> _definitions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Add a feature definition
    /// </summary>
    public void Add(
        string name,
        string? defaultValue = null,
        FeatureValueType valueType = FeatureValueType.Boolean,
        string? displayName = null,
        string? description = null,
        string? parentName = null,
        string? group = null)
    {
        Check.NotNullOrWhiteSpace(name);

        _definitions[name] = new FeatureDefinitionRecord(
            Name: name,
            DisplayName: displayName ?? name,
            Description: description,
            DefaultValue: defaultValue,
            ValueType: valueType,
            ParentName: parentName,
            IsEnabled: true,
            Group: group);
    }

    /// <summary>
    /// Get all collected definitions
    /// </summary>
    internal IReadOnlyDictionary<string, FeatureDefinitionRecord> GetDefinitions() => _definitions;
}
