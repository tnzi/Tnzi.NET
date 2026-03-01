namespace Tnzi.Feature.Features;

/// <summary>
/// Pluggable feature value provider.
/// Providers are evaluated in descending priority order.
/// </summary>
public interface IFeatureValueProvider
{
    /// <summary>
    /// Provider name (e.g., "Tenant", "Edition")
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Priority (higher values are evaluated first)
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Get the feature value for the current context, or null if not set
    /// </summary>
    /// <param name="featureName">Feature name</param>
    /// <returns>Feature value or null</returns>
    Task<string?> GetOrNullAsync(string featureName);
}
