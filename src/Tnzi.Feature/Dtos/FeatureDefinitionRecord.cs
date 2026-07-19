namespace Tnzi.Feature.Dtos;

/// <summary>
/// Immutable record for feature definition snapshot
/// </summary>
public record FeatureDefinitionRecord(
    string Name,
    string? DisplayName,
    string? Description,
    string? DefaultValue,
    FeatureValueType ValueType,
    string? ParentName,
    bool IsEnabled,
    string? Group);
