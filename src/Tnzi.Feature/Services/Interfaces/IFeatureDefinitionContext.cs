namespace Tnzi.Feature.Services;

/// <summary>
/// Context for collecting feature definitions from code providers
/// </summary>
public interface IFeatureDefinitionContext
{
    /// <summary>
    /// Add a feature definition
    /// </summary>
    /// <param name="name">Feature name (unique identifier)</param>
    /// <param name="defaultValue">Default value</param>
    /// <param name="valueType">Value type</param>
    /// <param name="displayName">Display name</param>
    /// <param name="description">Description</param>
    /// <param name="parentName">Parent feature name</param>
    /// <param name="group">Feature group</param>
    void Add(
        string name,
        string? defaultValue = null,
        FeatureValueType valueType = FeatureValueType.Boolean,
        string? displayName = null,
        string? description = null,
        string? parentName = null,
        string? group = null);
}
