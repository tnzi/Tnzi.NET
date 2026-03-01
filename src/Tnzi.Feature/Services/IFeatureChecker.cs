namespace Tnzi.Feature.Services;

/// <summary>
/// Feature checker interface for business code.
/// Provides simple methods to check if a feature is enabled or get its value.
/// </summary>
public interface IFeatureChecker
{
    /// <summary>
    /// Check if a feature is enabled.
    /// For Boolean features, returns the resolved value.
    /// For non-Boolean features, returns true if any value is set.
    /// </summary>
    /// <param name="featureName">Feature name</param>
    /// <returns>Whether the feature is enabled</returns>
    Task<bool> IsEnabledAsync(string featureName);

    /// <summary>
    /// Check if all specified features are enabled
    /// </summary>
    /// <param name="featureNames">Feature names to check</param>
    /// <returns>True if all features are enabled</returns>
    async Task<bool> IsAllEnabledAsync(params string[] featureNames)
    {
        foreach (var name in featureNames)
        {
            if (!await IsEnabledAsync(name))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Check if any of the specified features is enabled
    /// </summary>
    /// <param name="featureNames">Feature names to check</param>
    /// <returns>True if at least one feature is enabled</returns>
    async Task<bool> IsAnyEnabledAsync(params string[] featureNames)
    {
        foreach (var name in featureNames)
        {
            if (await IsEnabledAsync(name))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Get the feature value as a specific struct type
    /// </summary>
    /// <typeparam name="T">Target type (must be a value type)</typeparam>
    /// <param name="featureName">Feature name</param>
    /// <returns>Parsed value or null if not found or parse failed</returns>
    Task<T?> GetValueAsync<T>(string featureName) where T : struct;

    /// <summary>
    /// Get the feature value as a string
    /// </summary>
    /// <param name="featureName">Feature name</param>
    /// <returns>String value or null if not found</returns>
    Task<string?> GetValueAsync(string featureName);
}
