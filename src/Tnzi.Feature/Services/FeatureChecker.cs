namespace Tnzi.Feature.Services;

/// <summary>
/// Default implementation of IFeatureChecker.
/// Evaluates provider chain in descending priority order,
/// falling back to the feature definition's default value.
/// </summary>
public class FeatureChecker : IFeatureChecker
{
    private readonly IFeatureManager _featureManager;
    private readonly IReadOnlyList<IFeatureValueProvider> _sortedProviders;

    /// <summary>
    /// Initialize FeatureChecker
    /// </summary>
    public FeatureChecker(
        IFeatureManager featureManager,
        IEnumerable<IFeatureValueProvider> providers)
    {
        _featureManager = Check.NotNull(featureManager);
        Check.NotNull(providers);
        // Cache sorted providers to avoid re-sorting on every call
        _sortedProviders = providers.OrderByDescending(p => p.Priority).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<bool> IsEnabledAsync(string featureName)
    {
        Check.NotNullOrWhiteSpace(featureName);

        var value = await GetValueAsync(featureName);
        if (value == null)
            return false;

        if (bool.TryParse(value, out var boolValue))
            return boolValue;

        // For non-Boolean features, having any value means "enabled"
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <inheritdoc />
    public async Task<T?> GetValueAsync<T>(string featureName) where T : struct
    {
        var value = await GetValueAsync(featureName);
        if (value == null)
            return null;

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetValueAsync(string featureName)
    {
        Check.NotNullOrWhiteSpace(featureName);

        var definition = await _featureManager.GetOrNullAsync(featureName);
        if (definition == null || !definition.IsEnabled)
            return null;

        // Evaluate providers in descending priority order (pre-sorted)
        foreach (var provider in _sortedProviders)
        {
            var value = await provider.GetOrNullAsync(featureName);
            if (value != null)
                return value;
        }

        // Fall back to default value
        return definition.DefaultValue;
    }
}
