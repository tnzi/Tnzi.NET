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

        // 用量记录必须在当前调用链内 await：IFeatureUsageService 是 Scoped，用的是本请求的
        // DbContext。丢进 Task.Run 会让后台线程与请求线程并发使用同一个 DbContext
        //（非线程安全，可能连带把请求自身的操作打断），且请求结束后 scope 已释放 ——
        // 记录静默失败。RecordUsageAsync 内部自行吞掉并记录异常，await 它不会影响调用方。
        if (_featureUsageService != null)
        {
            await _featureUsageService.RecordUsageAsync(featureName, isEnabled, "FeatureChecker");
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
