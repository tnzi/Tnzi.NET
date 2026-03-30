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
    private readonly IFeatureUsageService? _featureUsageService;

    /// <summary>
    /// Initialize FeatureChecker
    /// </summary>
    public FeatureChecker(
        IFeatureManager featureManager,
        IEnumerable<IFeatureValueProvider> providers,
        IFeatureUsageService? featureUsageService = null)
    {
        _featureManager = Check.NotNull(featureManager);
        Check.NotNull(providers);
        // Cache sorted providers to avoid re-sorting on every call
        _sortedProviders = providers.OrderByDescending(p => p.Priority).ToList().AsReadOnly();
        _featureUsageService = featureUsageService;
    }

    /// <inheritdoc />
    public async Task<bool> IsEnabledAsync(string featureName)
    {
        Check.NotNullOrWhiteSpace(featureName);

        var value = await GetValueAsync(featureName);
        bool isEnabled;

        if (value == null)
        {
            isEnabled = false;
        }
        else if (bool.TryParse(value, out var boolValue))
        {
            isEnabled = boolValue;
        }
        else
        {
            // For non-Boolean features, having any value means "enabled"
            isEnabled = !string.IsNullOrWhiteSpace(value);
        }

        // Fire-and-forget usage recording (errors are silently caught in RecordUsageAsync)
        if (_featureUsageService != null)
        {
            _ = Task.Run(() => _featureUsageService.RecordUsageAsync(featureName, isEnabled, "FeatureChecker"));
        }

        return isEnabled;
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
