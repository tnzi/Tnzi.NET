namespace Tnzi.Feature.Features;

/// <summary>
/// Feature definition manager.
/// Manages feature definition snapshots, merging code-level and database definitions.
/// </summary>
public interface IFeatureManager
{
    /// <summary>
    /// Get all feature definitions (cached snapshot)
    /// </summary>
    Task<IReadOnlyList<FeatureDefinitionRecord>> GetAllAsync();

    /// <summary>
    /// Get a specific feature definition by name
    /// </summary>
    /// <param name="name">Feature name</param>
    /// <returns>Feature definition or null if not found</returns>
    Task<FeatureDefinitionRecord?> GetOrNullAsync(string name);

    /// <summary>
    /// Invalidate the cached snapshot, forcing a refresh on next access
    /// </summary>
    Task InvalidateCacheAsync();
}
